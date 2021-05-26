using System;
using System.Collections.Generic;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using ProjectN.Bolt;
using System.Diagnostics;

namespace MPLambdaPOC
{
    /// <summary>
    /// BoltAutoHealHandler is a handler class that encapsulates the handler function HandleRequest, which performs
    /// auto-heal tests and is called by AWS Lambda when the function is invoked.
    /// </summary>
    public class BoltAutoHealHandler
    {
        /// <summary>
        /// HandleRequest is the handler function that is invoked by AWS Lambda to process an incoming event for
        /// performing auto-heal tests.
        ///
        /// lambda_handler accepts the following input parameters as part of the event:
        /// 1) bucket - bucket name
        /// 2) key - key name
        ///
        /// </summary>
        /// <param name="input">incoming event</param>
        /// <param name="context">lambda context</param>
        /// <returns>time taken to auto-heal</returns>
        public Dictionary<string, string> HandleRequest(Dictionary<string, string> input, ILambdaContext context)
        {
            string bucket = input["bucket"];
            string key = input["key"];

            // Bolt Client
            AmazonS3Client boltS3Client = new BoltS3Client();

            Stopwatch stopwatch = new Stopwatch();

            // Attempt to retrieve object repeatedly until it succeeds, which would indicate successful
            // auto-healing of the object.
            stopwatch.Restart();
            while (true)
            {
                try
                {
                    // Get Object from Bolt.
                    var getObjectRequest = new GetObjectRequest
                    {
                        BucketName = bucket,
                        Key = key
                    };

                    var getObjectTask = boltS3Client.GetObjectAsync(getObjectRequest);
                    GetObjectResponse boltS3Response = getObjectTask.Result;

                    /*
                    // read all the data from the stream
                    using GetObjectResponse response = boltS3Response;
                    using Stream responseStream = response.ResponseStream;
                    using StreamReader reader = new StreamReader(responseStream);

                    // read all the data from the stream.
                    char[] readBuffer = new char[4096];
                    while (reader.Read(readBuffer, 0, readBuffer.Length) > 0) ;
                    */

                    // exit on success after auto-heal
                    stopwatch.Stop();
                    break;
                }
                catch (AmazonS3Exception)
                {
                    // Ingore AmazonS3Exception
                }
                catch (Exception)
                {
                    // Ignore Exception
                }
            }

            return new Dictionary<string, string>()
            {
                {"auto_heal_time", string.Format("{0:0.00} ms", stopwatch.ElapsedMilliseconds)}
            };
        }
    }
}
