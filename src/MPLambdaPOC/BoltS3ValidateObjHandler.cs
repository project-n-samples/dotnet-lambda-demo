using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using ProjectN.Bolt;

namespace MPLambdaPOC
{
    /// <summary>
    /// BoltS3ValidateObjHandler is a handler class that encapsulates the handler function HandleRequest, which performs
    /// data validation tests and is called by AWS Lambda when the function is invoked.
    /// </summary>
    public class BoltS3ValidateObjHandler
    {
        /// <summary>
        /// Indicates if source bucket is cleaned post crunch.
        /// </summary>
        enum BucketClean
        {
            // bucket is cleaned post crunch
            ON,
            // bucket is not cleaned post crunch
            OFF
        }

        /// <summary>
        /// HandleRequest is the handler function that is invoked by AWS Lambda to process an incoming event for
        /// performing data validation tests.
        ///
        /// HandleRequest accepts the following input parameters as part of the event:
        /// 1) bucket - bucket name
        /// 2) key - key name
        ///
        /// handleRequest retrieves the object from Bolt and S3 (if BucketClean is OFF), computes and returns their
        /// corresponding MD5 hash. If the object is gzip encoded, object is decompressed before computing its MD5.
        /// </summary>
        /// <param name="input">incoming event</param>
        /// <param name="context">lambda context</param>
        /// <returns>md5s of object retrieved from Bolt and S3.</returns>
        public Dictionary<string, string> HandleRequest(Dictionary<string, string> input, ILambdaContext context)
        {
            string bucket = input["bucket"];
            string key = input["key"];
            input.TryGetValue("bucketClean", out string bucketCleanStr);
            BucketClean bucketClean = string.IsNullOrEmpty(bucketCleanStr) ?
                BucketClean.OFF : (BucketClean)Enum.Parse(typeof(BucketClean), bucketCleanStr.ToUpper());

            AmazonS3Client s3Client = new AmazonS3Client();
            AmazonS3Client boltS3Client = new BoltS3Client();

            Dictionary<string, string> respDict;

            try
            {
                // Get Object from Bolt.
                Task<GetObjectResponse> getBoltS3ObjTask = boltS3Client.GetObjectAsync(bucket, key);
                GetObjectResponse boltS3Response = getBoltS3ObjTask.Result;

                // Get Object from S3 if bucket clean is off.
                GetObjectResponse s3Response = null;
                if (bucketClean == BucketClean.OFF)
                {
                    Task<GetObjectResponse> getS3ObjTask = s3Client.GetObjectAsync(bucket, key);
                    s3Response = getS3ObjTask.Result;
                }

                // Parse the MD5 of the returned object
                string s3Md5, boltS3Md5;
                using MD5 md5 = MD5.Create();
                string encoding = s3Response.Headers.ContentEncoding;
                byte[] s3Data, boltS3Data;
                if ((!string.IsNullOrEmpty(encoding) &&
                    encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                    || key.EndsWith(".gz"))
                {
                    // decompress gzipped Bolt/S3 Objects
                    using var s3GzipStream = new GZipStream(s3Response.ResponseStream, CompressionMode.Decompress);
                    using var boltS3GzipStream = new GZipStream(boltS3Response.ResponseStream, CompressionMode.Decompress);
                    using var s3OutputStream = new MemoryStream();
                    using var boltS3OutputStream = new MemoryStream();

                    s3GzipStream.CopyTo(s3OutputStream);
                    boltS3GzipStream.CopyTo(boltS3OutputStream);

                    s3Data = md5.ComputeHash(s3OutputStream.ToArray());
                    boltS3Data = md5.ComputeHash(boltS3OutputStream.ToArray());
                }
                else
                {
                    s3Data = md5.ComputeHash(s3Response.ResponseStream);
                    boltS3Data = md5.ComputeHash(boltS3Response.ResponseStream);
                }

                // MD5 of the S3 object.
                var s3StrBuilder = new StringBuilder();
                for (int i = 0; i < s3Data.Length; i++)
                {
                    s3StrBuilder.Append(s3Data[i].ToString("x2"));
                }
                s3Md5 = s3StrBuilder.ToString().ToUpper();

                // MD5 of the Bolt object.
                var boltS3StrBuilder = new StringBuilder();
                for (int i = 0; i < boltS3Data.Length; i++)
                {
                    boltS3StrBuilder.Append(boltS3Data[i].ToString("x2"));
                }
                boltS3Md5 = boltS3StrBuilder.ToString().ToUpper();

                respDict = new Dictionary<string, string>()
                {
                    {"s3-md5", s3Md5},
                    {"bolt-md5", boltS3Md5}
                };
            }
            catch (AmazonS3Exception e)
            {
                respDict = new Dictionary<string, string>()
                {
                    {"errorMessage", e.Message},
                    {"errorCode", e.ErrorCode}
                };
            }
            catch (Exception e)
            {
                respDict = new Dictionary<string, string>()
                {
                    {"errorMessage", e.Message}
                };
            }
            return respDict;
        }
    }
}
