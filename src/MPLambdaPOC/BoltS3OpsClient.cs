using System;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Model;
using ProjectN.Bolt;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.IO;

namespace MPLambdaPOC
{
    /// <summary>
    /// BoltS3OpsClient processes AWS Lambda events that are received by the handler function
    /// BoltS3OpsHandler.handleRequest.
    /// </summary>
    public class BoltS3OpsClient
    {
        // request types supported
        enum RequestType
        {
            HEAD_OBJECT,
            GET_OBJECT,
            LIST_OBJECTS_V2,
            LIST_BUCKETS,
            HEAD_BUCKET,
            PUT_OBJECT,
            DELETE_OBJECT
        }

        // endpoints supported
        enum SdkType
        {
            BOLT,
            S3
        }

        private AmazonS3Client client;

        public BoltS3OpsClient()
        {
            client = null;
        }

        /// <summary>
        /// processEvent extracts the parameters (sdkType, requestType, bucket/key) from the event, uses those
        /// parameters to send an Object/Bucket CRUD request to Bolt/S3 and returns back an appropriate response.
        /// </summary>
        /// <param name="input">incoming Lambda event</param>
        /// <returns>result of the requested operation returned by the endpoint (sdkType)</returns>
        public Dictionary<string, string> ProcessEvent(Dictionary<string, string> input)
        {
            RequestType requestType = (RequestType)Enum.Parse(typeof(RequestType), input["requestType"].ToUpper());
            input.TryGetValue("sdkType", out string sdkTypeStr);
            SdkType sdkType = string.IsNullOrEmpty(sdkTypeStr) ? SdkType.S3 : (SdkType)Enum.Parse(typeof(SdkType), sdkTypeStr.ToUpper());

            // create an S3/Bolt Client depending on the 'sdkType'
            // If sdkType is not specified, create an S3 Client.
            if (sdkType == SdkType.S3)
            {
                client = new AmazonS3Client();
            } else if (sdkType == SdkType.BOLT)
            {
                client = new BoltS3Client();
            }

            // Perform an S3 / Bolt operation based on the input 'requestType'
            Dictionary<string, string> respDict;
            Task<Dictionary<string, string>> t1;
            try
            {
                switch (requestType)
                {
                    case RequestType.LIST_OBJECTS_V2:
                        t1 = ListObjectsV2(input["bucket"]);
                        respDict = t1.Result;
                        break;
                    case RequestType.LIST_BUCKETS:
                        t1 = ListBuckets();
                        respDict = t1.Result;
                        break;
                    case RequestType.HEAD_BUCKET:
                        t1 = HeadBucket(input["bucket"]);
                        respDict = t1.Result;
                        break;
                    case RequestType.GET_OBJECT:
                        t1 = GetObject(input["bucket"], input["key"]);
                        respDict = t1.Result;
                        break;
                    case RequestType.HEAD_OBJECT:
                        t1 = HeadObject(input["bucket"], input["key"]);
                        respDict = t1.Result;
                        break;
                    case RequestType.PUT_OBJECT:
                        t1 = PutObject(input["bucket"], input["key"], input["value"]);
                        respDict = t1.Result;
                        break;
                    case RequestType.DELETE_OBJECT:
                        t1 = DeleteObject(input["bucket"], input["key"]);
                        respDict = t1.Result;
                        break;
                    default:
                        respDict = new Dictionary<string, string>();
                        break;
                }
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

        /// <summary>
        /// Returns a list of 1000 objects from the given bucket in Bolt/S3
        /// </summary>
        /// <param name="bucket">bucket name</param>
        /// <returns>list of first 1000 objects</returns>
        private async Task<Dictionary<string, string>> ListObjectsV2(string bucket)
        {
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket });

            return new Dictionary<string, string>()
            {
                {"objects", string.Concat(response.S3Objects.Select(o=>o.Key + ", "))}
            };
        }

        /// <summary>
        /// Returns list of buckets owned by the sender of the request
        /// </summary>
        /// <returns>list of buckets</returns>
        private async Task<Dictionary<string, string>> ListBuckets()
        {
            var response = await client.ListBucketsAsync();

            return new Dictionary<string, string>()
            {
                {"buckets", string.Concat(response.Buckets.Select(b=>b.BucketName + ", "))}
            };
        }

        /// <summary>
        /// Retrieves the object's metadata from Bolt / S3.
        /// </summary>
        /// <param name="bucket">bucket name</param>
        /// <param name="key">key name</param>
        /// <returns>object metadata</returns>
        private async Task<Dictionary<string, string>> HeadObject(string bucket, string key)
        {
            var response = await client.GetObjectMetadataAsync(bucket, key);

            return new Dictionary<string, string>()
            {
                {"Expiration", response.Expiration != null ?
                response.Expiration.ExpiryDateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") : ""},
                {"lastModified", response.LastModified.ToString("yyyy-MM-ddTHH:mm:ssZ")},
                {"ContentLength", response.ContentLength.ToString()},
                {"ContentEncoding", response.Headers.ContentEncoding},
                {"ETag", response.ETag},
                {"VersionId", response.VersionId},
                {"StorageClass", response.StorageClass}
            };
        }

        /// <summary>
        /// Gets the object from Bolt/S3, computes and returns the object's MD5 hash. If the object is gzip encoded, object
        /// is decompressed before computing its MD5.
        /// </summary>
        /// <param name="bucket">bucket name</param>
        /// <param name="key">key name</param>
        /// <returns>md5 hash of the object</returns>
        private async Task<Dictionary<string, string>> GetObject(string bucket, string key)
        {
            // Get Object.
            var response = await client.GetObjectAsync(bucket, key);

            // Parse the MD5 of the returned object
            string objMD5;
            using (MD5 md5 = MD5.Create())
            {
                // If Object is gzip encoded, compute MD5 on the decompressed object.
                string encoding = response.Headers.ContentEncoding;
                byte[] data;
                if ((!string.IsNullOrEmpty(encoding) &&
                    encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                    || key.EndsWith(".gz"))
                {
                    // MD5 of the object after gzip decompression.
                    using var gzipStream = new GZipStream(response.ResponseStream, CompressionMode.Decompress);
                    using var outputStream = new MemoryStream();
                    gzipStream.CopyTo(outputStream);
                    data = md5.ComputeHash(outputStream.ToArray());
                }
                else
                {
                    data = md5.ComputeHash(response.ResponseStream);
                }

                var sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }
                objMD5 = sBuilder.ToString().ToUpper();
            }

            return new Dictionary<string, string>()
            {
                {"md5", objMD5}
            };
        }

        /// <summary>
        /// Checks if the bucket exists in Bolt/S3.
        /// </summary>
        /// <param name="bucket">bucket name</param>
        /// <returns>status code and Region if the bucket exists</returns>
        private async Task<Dictionary<string, string>> HeadBucket(string bucket)
        {
            var response = await client.GetBucketLocationAsync(bucket);

            return new Dictionary<string, string>()
            {
                {"statusCode", response.HttpStatusCode.ToString()},
                {"region", response.Location}
            };
        }

        /// <summary>
        /// Delete an object from Bolt/S3
        /// </summary>
        /// <param name="bucket">bucket name</param>
        /// <param name="key">key name</param>
        /// <returns>status code</returns>
        private async Task<Dictionary<string, string>> DeleteObject(string bucket, string key)
        {
            var response = await client.DeleteObjectAsync(bucket, key);

            return new Dictionary<string, string>()
            {
                { "statusCode", response.HttpStatusCode.ToString()}
            };
        }

        /// <summary>
        /// Uploads an object to Bolt/S3.
        /// </summary>
        /// <param name="bucket">bucket name</param>
        /// <param name="key">key name</param>
        /// <param name="value">object data</param>
        /// <returns>metadata of the object</returns>
        private async Task<Dictionary<string, string>> PutObject(string bucket, string key, string value)
        {
            var putObjectRequest = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ContentBody = value
            };

            var response = await client.PutObjectAsync(putObjectRequest);

            return new Dictionary<string, string>()
            {
                {"ETag", response.ETag},
                {"Expiration", response.Expiration != null ?
                response.Expiration.ExpiryDateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") : ""},
                {"VersionId", response.VersionId}
            };
        }
    }
}
