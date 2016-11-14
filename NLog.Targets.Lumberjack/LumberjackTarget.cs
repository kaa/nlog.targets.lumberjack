using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;
using NLog.Config;

namespace NLog.Targets.Lumberjack {
	[Target("Lumberjack")]
	public class LumberjackTarget : TargetWithLayout {
		private SemaphoreSlim streamSemaphore = new SemaphoreSlim(1);
		private SemaphoreSlim writeSemaphore = new SemaphoreSlim(1);
		[RequiredParameter]
		public string Host { get; set; }
		public int Port { get; set; }
		public string Fingerprint { get; set; }
		public Encoding Encoding { get; set; }
		public LumberjackTarget() {
			Encoding = Encoding.UTF8;
			Port = 5000;
		}

		private SslStream stream;
		private async Task<SslStream> GetStream() {
			await streamSemaphore.WaitAsync();
			try {
				if(stream!=null) return stream;
				var tcpClient = new TcpClient();
				await tcpClient.ConnectAsync(Host, Port);
				stream = new SslStream(tcpClient.GetStream(), false, (source, cert, chain, policy) => {
					return Fingerprint==null || Fingerprint.Equals(cert.GetCertHashString(), StringComparison.OrdinalIgnoreCase);
				});
				await stream.AuthenticateAsClientAsync("", new X509CertificateCollection(), SslProtocols.Tls, true);
				DrainStream(stream);
				return stream;
			} finally {
				streamSemaphore.Release();
			}
		}

		private void DrainStream(Stream stream) { 
			Task.Run(() => {
				try {
					byte[] buf = new byte[32];
					while(true) stream.Read(buf,0,buf.Length);
				} catch(Exception e) {
					InternalLogger.Warn("Unable to drain stream, exception {0}.", e.Message);
				}
			});
		}

		protected override void CloseTarget() {
			base.CloseTarget();

			streamSemaphore.Wait();
			try {
				if(stream == null) return;
				stream.Dispose();
				stream = null;
			} finally {
				streamSemaphore.Release();
			}
		}

		protected override void Write(LogEventInfo logEvent) {
			throw new NotSupportedException("Synchronous write operation is not supported.");
		}
		protected override void Write(NLog.Common.AsyncLogEventInfo logEvent) {
			WriteAsync(logEvent.LogEvent).ContinueWith(task => {
				logEvent.Continuation(task.Exception);
			});
		}

		private DateTime lastExceptionAt;
		private async Task WriteAsync(LogEventInfo logEvent) {
			if(DateTime.Now.Subtract(lastExceptionAt).TotalSeconds < 60)
				return;

			var stream = new MemoryStream();
			stream.WriteByte(1);
			stream.WriteByte((byte)'D');
			var sequenceAndCountBuffer = new byte[8];
			sequenceAndCountBuffer[0] = (byte)(logEvent.SequenceID >> 24);
			sequenceAndCountBuffer[1] = (byte)(logEvent.SequenceID >> 16);
			sequenceAndCountBuffer[2] = (byte)(logEvent.SequenceID >> 8);
			sequenceAndCountBuffer[3] = (byte)(logEvent.SequenceID);
			sequenceAndCountBuffer[7] = (byte)(4 + logEvent.Properties.Count);
			stream.Write(sequenceAndCountBuffer, 0, 8);
			WriteKeyValuePair(stream, "logger", logEvent.LoggerName);
			WriteKeyValuePair(stream, "offset", "0");
			WriteKeyValuePair(stream, "host", Environment.MachineName);
			WriteKeyValuePair(stream, "line", Layout.Render(logEvent));
			WriteKeyValuePair(stream, "level", logEvent.Level.Name);
			WriteKeyValuePair(stream, "eventTimestamp", logEvent.TimeStamp.ToString("g"));
			if(logEvent.Message != null) {
				WriteKeyValuePair(stream, "exceptionMessage", logEvent.Exception.Message);
				WriteKeyValuePair(stream, "exceptionStack", logEvent.Exception.StackTrace);
			}
			foreach(var property in logEvent.Properties) {
				WriteKeyValuePair(stream, property.Key.ToString(), property.Value.ToString());
			}

			var sslStream = await GetStream();
			try {
				await writeSemaphore.WaitAsync();
				await sslStream.WriteAsync(stream.ToArray(), 0, (int)stream.Length);
			} catch(Exception e) { 
				InternalLogger.Error("Exception {0} while sending lumberjack frame.", e.Message);
				lastExceptionAt = DateTime.Now;
				try {
					stream.Close();
					stream = null;
				} catch { 
				}
			} finally { 
				writeSemaphore.Release();
			}
		}

		private void WriteKeyValuePair(Stream stream, string key, string value) {
			var lengthBuffer = new byte[4];
			byte[] dataBuffer;

			dataBuffer = Encoding.GetBytes(key);
			lengthBuffer[0] = (byte)(dataBuffer.Length>>24);
			lengthBuffer[1] = (byte)(dataBuffer.Length>>16);
			lengthBuffer[2] = (byte)(dataBuffer.Length>>8);
			lengthBuffer[3] = (byte)(dataBuffer.Length>>0);
			stream.Write(lengthBuffer, 0, lengthBuffer.Length);
			stream.Write(dataBuffer, 0, dataBuffer.Length);

			dataBuffer = Encoding.GetBytes(value);
			lengthBuffer[0] = (byte)(dataBuffer.Length>>24);
			lengthBuffer[1] = (byte)(dataBuffer.Length>>16);
			lengthBuffer[2] = (byte)(dataBuffer.Length>>8);
			lengthBuffer[3] = (byte)(dataBuffer.Length>>0);
			stream.Write(lengthBuffer, 0, lengthBuffer.Length);
			stream.Write(dataBuffer, 0, dataBuffer.Length);
		}
	}
}
