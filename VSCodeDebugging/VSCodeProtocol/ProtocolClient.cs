using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

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

	public class ProtocolClient : ProtocolEndpoint
	{
		readonly Dictionary<int, Request> Requests = new Dictionary<int, Request>();
		protected override void Dispatch(string message)
		{
			var jObject = JObject.Parse(message);
			var type = jObject.Value<string>("type");
			if (type == "response") {
				var success = jObject.Value<bool>("success");
				var request_seq = jObject.Value<int>("request_seq");
				var request = Requests[request_seq];
				if (success) {
					request.ObjectBody = jObject.GetValue("body").ToObject(request.GetResponseBodyType());
				} else {
					request.ErrorMessage = jObject.Value<string>("message");
				}
				//if (Trace.HasFlag(TraceLevel.Responses))
				//	Console.Error.WriteLine(string.Format("R {0}: {1}", response.command, JsonConvert.SerializeObject(response.body)));
			} else if (type == "event")
			{
				var eventBodyJObject = jObject.GetValue("body");
				var eventTypeString = eventBodyJObject.Value<string>("type");
				Type eventType;

				if (EventBody.EventTypeMap.TryGetValue(eventTypeString, out eventType))
				{
					var eventBody = (EventBody)eventBodyJObject.ToObject(eventType);
					OnEvent?.Invoke(eventBody);
				}
				else
				{
					throw new ArgumentOutOfRangeException(string.Format("Unknown event type: {0}", type));
				}
			}
		}

		public Action<EventBody> OnEvent;

		public Task<T2> SendRequestAsync<T1, T2>(Request<T1, T2> request) where T2 : new()
		{
			request.seq = _sequenceNumber++;
			Requests.Add(request.seq, request);
			SendMessage(request);
			return request.WaitingResponse.Task;
		}
	}
}