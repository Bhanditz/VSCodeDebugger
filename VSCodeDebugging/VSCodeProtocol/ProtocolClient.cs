using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SystemThread = System.Threading.Thread;
namespace VSCodeDebugging.VSCodeProtocol
{
	///*
	//    * The ProtocolServer can be used to implement a server that uses the VSCode debug protocol.
	//    */
	//public abstract class ProtocolServer : ProtocolEndpoint
	//{
	//	protected abstract void DispatchRequest(string command, dynamic args, Response response);

	//	protected override void Dispatch(string message)
	//	{
	//		var request = JsonConvert.DeserializeObject<Request>(message);
	//		if (request != null && request.type == "request") {
	//			if (Trace.HasFlag(TraceLevel.Requests))
	//				Console.Error.WriteLine(string.Format("C {0}: {1}", request.command, JsonConvert.SerializeObject(request.arguments)));

	//			var response = new Response(request);

	//			DispatchRequest(request.command, request.arguments, response);

	//			SendMessage(response);
	//		}
	//	}

	//	public void SendEvent(Event e)
	//	{
	//		SendMessage(e);
	//	}
	//}

	class ProtocolMessageConverter : JsonCreationConverter<ProtocolMessage>
	{
		protected override ProtocolMessage Create(Type objectType, JObject jObject, JsonSerializer serializer, ConverterContext context)
		{
			var type = jObject["type"].Value<string>();
			switch (type)
			{
				case "response" :
				{
					context.Data[ResponseBodyConverter.CommandFieldName] = jObject["command"].Value<string>();
					return new Response();
				}
				case "event" : return new Event();
				case "request" : throw new NotSupportedException("Requests are not expected as input");
				default: throw new NotSupportedException(string.Format("Message type '{0}' not supported", type));
 			}
		}

		protected override bool PopulateAfterCreation => true;
	}

	class ResponseBodyConverter : JsonCreationConverter<ResponseBody>
	{
		public const string CommandFieldName = "command";

		protected override ResponseBody Create(Type objectType, JObject jObject, JsonSerializer serializer, ConverterContext context)
		{
			var parentContext = context.ParentContext;
			if (parentContext == null)
			{
				throw new ArgumentException("Parent context can't be null for this converter");
			}
			object value;
			if (!parentContext.Data.TryGetValue(CommandFieldName, out value))
			{
				throw new InvalidOperationException(string.Format("{0} have to be specified in context for this converter", CommandFieldName));
			}
			var command = (string)value;
			Type responveType;
			if (ResponseBody.CommandToType.TryGetValue(command, out responveType))
			{
				return (ResponseBody) jObject.ToObject(responveType);
			}
			return jObject.ToObject<ResponseBody>();
		}

		protected override bool PopulateAfterCreation => false;
	}

	class EventBodyConverter : JsonCreationConverter<EventBody>
	{
		protected override EventBody Create(Type objectType, JObject jObject, JsonSerializer serializer, ConverterContext context)
		{
			var type = jObject["type"].Value<string>();
			Type eventBodyType;
			if (EventBody.EventTypeMap.TryGetValue(type, out eventBodyType))
			{
				return (EventBody) jObject.ToObject(eventBodyType);
			}
			return jObject.ToObject<EventBody>();
		}

		protected override bool PopulateAfterCreation => false;
	}

	public class ProtocolClient : ProtocolEndpoint
	{
		readonly ConcurrentDictionary<int, Request> _requests = new ConcurrentDictionary<int, Request>();

		readonly BlockingCollection<ProtocolMessage> _messageQueue = new BlockingCollection<ProtocolMessage>(new ConcurrentQueue<ProtocolMessage>());
		readonly BlockingCollection<ProtocolMessage> _syncMessageQueue = new BlockingCollection<ProtocolMessage>(new ConcurrentQueue<ProtocolMessage>());
		bool _insideSyncRequest = false;
		int _pollerThreadId = -1;

		public override void Start(Stream inputStream, Stream outputStream)
		{
			base.Start(inputStream, outputStream);
			Task.Run(() => QueuePoller());
		}

		void QueuePoller()
		{
			_pollerThreadId = SystemThread.CurrentThread.ManagedThreadId;
			while (true)
			{
				var protocolMessage = _messageQueue.Take();

				if (protocolMessage is Response)
				{
					var response = (Response) protocolMessage;

					var success = response.success;
					var requestSeq = response.request_seq;
					Request request;
					if (!_requests.TryRemove(requestSeq, out request))
					{
						OnDispatchException(new ThreadExceptionEventArgs(new InvalidOperationException($"Request with id ${requestSeq} is not registered")));
					}
					else
					{
						if (success)
						{
							request.ObjectBody = response.body;
						}
						else
						{
							request.ErrorMessage = response.message;
						}
					}
					//if (Trace.HasFlag(TraceLevel.Responses))
					//	Console.Error.WriteLine(string.Format("R {0}: {1}", response.command, JsonConvert.SerializeObject(response.body)));
				}
				else if (protocolMessage is Event)
				{
					var @event = (Event) protocolMessage;
					OnEvent?.Invoke(@event.body);
				}
			}
		}

		protected override void Dispatch(string message)
		{
			var protocolMessage = JsonConvert.DeserializeObject<ProtocolMessage>(message,
				new ProtocolMessageConverter(), new ResponseBodyConverter(), new EventBodyConverter());
			lock (_pumpLock)
			{
				if (_insideSyncRequest)
				{
					_syncMessageQueue.Add(protocolMessage);
				}
				else
				{
					_messageQueue.Add(protocolMessage);
				}
			}
		}

		public Action<EventBody> OnEvent;
		static readonly TimeSpan SyncEventTimeout = TimeSpan.FromSeconds(3);

		readonly object _pumpLock = new object();

		void RepumpQueues()
		{
			ProtocolMessage message;
			while (_syncMessageQueue.TryTake(out message))
			{
				_messageQueue.Add(message);
			}
		}

		private TResponseBody SendRequestSyncInternal<TRequestBody, TResponseBody>(Request<TRequestBody, TResponseBody> request)
			where TResponseBody : ResponseBody, new()
		{
			if (SystemThread.CurrentThread.ManagedThreadId != _pollerThreadId)
			{
				throw new InvalidOperationException("Should be called only inside event handler");
			}
			if (_insideSyncRequest)
			{
				throw new InvalidOperationException("Can not perform sync request while another is active");
			}
			try
			{
				_insideSyncRequest = true;
				RegisterRequest(request);
				SendMessage(request);
				while (true)
				{
					ProtocolMessage message;
					if (!_syncMessageQueue.TryTake(out message, SyncEventTimeout))
					{
						throw new TimeoutException($"Sync request was timed out: ${request.GetType()}");
					}
					if (message is Response)
					{
						var response = (Response)message;
						if (response.request_seq == request.seq)
						{
							return (TResponseBody) response.body;
						}
					}
					_messageQueue.Add(message);
				}
			}
			finally
			{
				lock (_pumpLock)
				{
					_insideSyncRequest = false;
					RepumpQueues();
				}
			}
		}

		void RegisterRequest(Request request)
		{
			request.seq = Interlocked.Increment(ref _sequenceNumber);
			_requests[request.seq] = request;
		}

		public TResponseBody SendRequestSync<TRequestBody, TResponseBody>(Request<TRequestBody, TResponseBody> request)
			where TResponseBody : ResponseBody, new()
		{
			if (SystemThread.CurrentThread.ManagedThreadId == _pollerThreadId)
			{
				return SendRequestSyncInternal(request);
			}
			return SendRequestAsync(request).Result;
		}

		/// <summary>
		/// DON'T USE this method for sync calls because it may cause a deadlock in the case when your call is performed inside debugger event handler (reentrancy)
		/// </summary>
		/// <param name="request"></param>
		/// <typeparam name="TRequestBody"></typeparam>
		/// <typeparam name="TResponseBody"></typeparam>
		/// <returns></returns>
		public Task<TResponseBody> SendRequestAsync<TRequestBody, TResponseBody>(Request<TRequestBody, TResponseBody> request)
			where TResponseBody : ResponseBody, new()
		{
			RegisterRequest(request);
			SendMessage(request);
			return request.WaitingResponse.Task;
		}
	}
}
