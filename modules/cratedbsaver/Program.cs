namespace cratedbsaver
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Net.Http;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;

    class Program
    {
        static int counter;

        private static HttpClient client = new HttpClient();

        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Register callback to be called when a message is received by the module
            await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                // Read Crate DB credentials from App settings and put it into the default headers for 
                // Basic authentication
                string crateDBCredentials = System.Environment.GetEnvironmentVariable("CrateDBCredentials");
                var byteArray = Encoding.ASCII.GetBytes(crateDBCredentials);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

                // Use the enqueued timestamp from IoT Hub as event timestamp
                string ts = message.Properties["iothub-enqueuedtime"];

                // The device ID is taken from the message properties
                string deviceID = message.Properties["iothub-connection-device-id"].ToString();

                // Build the HTTP body message for a Crate DB insert
                FormattableString fmtString = $"{{ \"stmt\" : \"insert into doc.raw ( iothub_enqueuedtime,iothub_connection_device_id,payload ) values ( TO_TIMESTAMP('{ts}'), '{deviceID}', {messageString} )\"}}";
                string jsonInString = fmtString.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US"));

                // Call the REST interface of the Crate DB
                string crateDBURL = System.Environment.GetEnvironmentVariable("CrateDBURL");
                HttpResponseMessage response = await client.PostAsync(crateDBURL,
                    new StringContent(jsonInString, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error {response.StatusCode} on inserting in Crate DB. Statement: {jsonInString}");
                } else
                {
                    Console.WriteLine("Successfully written message to Crate DB");
                }
            }
            return MessageResponse.Completed;
        }
    }
}
