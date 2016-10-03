using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VSCodeDebugging.VSCodeProtocol
{
	public abstract class ProtocolEndpoint
	{
		public TraceLevel Trace = TraceLevel.None;

		public event ThreadExceptionEventHandler DispatchException = delegate { };
		public event ThreadExceptionEventHandler SendException = delegate { };
		public event ThreadExceptionEventHandler ReceiveException = delegate { };

		protected const int BUFFER_SIZE = 4096;
		protected const string TWO_CRLF = "\r\n\r\n";
		protected static readonly Regex CONTENT_LENGTH_MATCHER = new Regex(@"Content-Length: (\d+)");

		protected static readonly Encoding Encoding = System.Text.Encoding.UTF8;

		protected int _sequenceNumber;

		private Stream _outputStream;

		private ByteBuffer _rawData;
		private int _bodyLength;

		private bool _stopRequested;


		public ProtocolEndpoint()
		{
			_sequenceNumber = 1;
			_bodyLength = -1;
			_rawData = new ByteBuffer();
		}

		public virtual void Start(Stream inputStream, Stream outputStream)
		{
			_outputStream = outputStream;

			byte[] buffer = new byte[BUFFER_SIZE];

			_stopRequested = false;

			Task.Run(() => {
				try
				{
					while (!_stopRequested)
					{

						var read = inputStream.Read(buffer, 0, buffer.Length);

						if (read == 0)
						{
							// end of stream
							break;
						}

						if (read > 0)
						{
							_rawData.Append(buffer, read);
							ProcessData();
						}
					}

				}
				catch (Exception e)
				{
					OnReceiveException(new ThreadExceptionEventArgs(e));
				}
			});
		}

		public void Stop()
		{
			_stopRequested = true;
		}

		// ---- private ------------------------------------------------------------------------

		private void ProcessData()
		{
			while (true) {
				if (_bodyLength >= 0) {
					if (_rawData.Length >= _bodyLength) {
						var buf = _rawData.RemoveFirst(_bodyLength);

						_bodyLength = -1;
						var str = Encoding.GetString(buf);

						try
						{
							Dispatch(str);
						}
						catch (Exception e)
						{
							OnDispatchException(new ThreadExceptionEventArgs(e));
						}

						continue;   // there may be more complete messages to process
					}
				} else {
					string s = _rawData.GetString(Encoding);
					var idx = s.IndexOf(TWO_CRLF, StringComparison.Ordinal);
					if (idx != -1) {
						Match m = CONTENT_LENGTH_MATCHER.Match(s);
						if (m.Success && m.Groups.Count == 2) {
							_bodyLength = Convert.ToInt32(m.Groups[1].ToString());

							_rawData.RemoveFirst(idx + TWO_CRLF.Length);

							continue;   // try to handle a complete message
						}
					}
				}
				break;
			}
		}

		protected abstract void Dispatch(string message);

		protected void SendMessage(ProtocolMessage message)
		{
			if (Trace.HasFlag(TraceLevel.Responses) && message.type == "response") {
				Console.Error.WriteLine(string.Format(" R: {0}", JsonConvert.SerializeObject(message)));
			}
			if (Trace.HasFlag(TraceLevel.Requests) && message.type == "request") {
				Console.Error.WriteLine(string.Format(" Q: {0}", JsonConvert.SerializeObject(message)));
			}
			if (Trace.HasFlag(TraceLevel.Events) && message.type == "event") {
				Event e = (Event)message;
				Console.Error.WriteLine(string.Format("E {0}: {1}", e.eventType, JsonConvert.SerializeObject(e.body)));
			}

			var data = ConvertToBytes(message);
			try {
				_outputStream.Write(data, 0, data.Length);
				_outputStream.Flush();
			} catch (Exception e) {
				OnSendException(new ThreadExceptionEventArgs(e));
			}
		}

		private static byte[] ConvertToBytes(ProtocolMessage request)
		{
			var asJson = JsonConvert.SerializeObject(request);
			byte[] jsonBytes = Encoding.GetBytes(asJson);

			string header = string.Format("Content-Length: {0}{1}", jsonBytes.Length, TWO_CRLF);
			byte[] headerBytes = Encoding.GetBytes(header);

			byte[] data = new byte[headerBytes.Length + jsonBytes.Length];
			System.Buffer.BlockCopy(headerBytes, 0, data, 0, headerBytes.Length);
			System.Buffer.BlockCopy(jsonBytes, 0, data, headerBytes.Length, jsonBytes.Length);

			return data;
		}

		protected virtual void OnDispatchException(ThreadExceptionEventArgs e)
		{
			DispatchException(this, e);
		}

		protected virtual void OnSendException(ThreadExceptionEventArgs e)
		{
			SendException(this, e);
		}

		protected virtual void OnReceiveException(ThreadExceptionEventArgs e)
		{
			ReceiveException(this, e);
		}
	}
}
