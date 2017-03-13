using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace ServerSocket
{
	class MainClass
	{
		public static void Main(string[] args)
		{
			CancellationTokenSource cts = new CancellationTokenSource();
			TcpListener listener = new TcpListener(IPAddress.Any,6666);
			try
			{
				listener.Start();
				Console.WriteLine("Server listening");
				//just fire and forget. We break from the "forgotten" async loops
				//in AcceptClientsAsync using a CancellationToken from `cts`
				AcceptClientsAsync(listener, cts.Token);
				Thread.Sleep(60000); //block here to hold open the server
				Console.WriteLine("Closing server");
			}

			finally
			{
				cts.Cancel();
				listener.Stop();
			}
		}

		static async void AcceptClientsAsync(TcpListener listener, CancellationToken ct)
		{
			var clientCounter = 0;
			while (!ct.IsCancellationRequested)
			{
				TcpClient client = await listener.AcceptTcpClientAsync()
													.ConfigureAwait(false);
				clientCounter++;
				//once again, just fire and forget, and use the CancellationToken
				//to signal to the "forgotten" async invocation.
				await EchoAsync(client, clientCounter, ct);
			}

		}
		static async Task EchoAsync(TcpClient client,
							 int clientIndex,
							 CancellationToken ct)
		{
			Console.WriteLine("New client ({0}) connected", clientIndex);
			using (client)
			{
				var buf = new byte[4096];
				var stream = client.GetStream();
				while (!ct.IsCancellationRequested)
				{
					//under some circumstances, it's not possible to detect
					//a client disconnecting if there's no data being sent
					//so it's a good idea to give them a timeout to ensure that 
					//we clean them up.
					var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
					var amountReadTask = stream.ReadAsync(buf, 0, buf.Length, ct);
					var completedTask = await Task.WhenAny(timeoutTask, amountReadTask)
												  .ConfigureAwait(false);
					if (completedTask == timeoutTask)
					{
						var msg = Encoding.ASCII.GetBytes(@"Client timed out: ");
						await stream.WriteAsync(msg, 0, msg.Length);
						break;
					}
					//now we know that the amountTask is complete so
					//we can ask for its Result without blocking
					var amountRead = amountReadTask.Result;
					string recv = Encoding.ASCII.GetString(buf.Take(amountRead).ToArray());					
					Console.Write("El client {0} envia: {1}",clientIndex,recv);

					//if client sends quit close connection
					if (recv.TrimEnd().Equals("quit", StringComparison.OrdinalIgnoreCase))
					{
						var msg = Encoding.ASCII.GetBytes(@"Client quitting: ");
						await stream.WriteAsync(msg, 0, msg.Length);
						break;

					}

					byte[] response = Encoding.ASCII.GetBytes("server says: " + recv);
					if (amountRead == 0) break; //end of stream.
					await stream.WriteAsync(response, 0, response.Length, ct)
								.ConfigureAwait(false);
				}
				stream.Dispose();
			}
			Console.WriteLine("Client ({0}) disconnected", clientIndex);

		}

	}
}
