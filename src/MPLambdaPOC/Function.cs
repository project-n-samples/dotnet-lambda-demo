using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using ProjectN.Bolt;
using System;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace MPLambdaPOC
{
    public class Function
    {
        static readonly AmazonS3Client Client = new BoltS3Client();

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<bool> ListBucket(string input, ILambdaContext context)
        {
            try
            {
                var response = await Client.ListObjectsV2Async(new ListObjectsV2Request {BucketName = input});
                response.S3Objects.ForEach(o => Console.WriteLine(o.Key));
                return true;
            }
            catch (AmazonS3Exception e)
            {
                Console.WriteLine("Encountered error: {0}", e.Message);
                return false;
            }
        }
    }
}