using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using ProjectN.Bolt;
using System.Text;
using System.IO;

namespace MPLambdaPOC
{
    public class BoltS3Perf
    {

        enum RequestType
        {
            LIST_OBJECTS_V2,
            PUT_OBJECT,
            DELETE_OBJECT,
            GET_OBJECT,
            GET_OBJECT_TTFB,
            GET_OBJECT_PASSTHROUGH,
            GET_OBJECT_PASSTHROUGH_TTFB,
            ALL
        }

        private AmazonS3Client s3Client;
        private AmazonS3Client boltS3Client;

        private int numKeys;
        private int objLength;
        RequestType requestType;

        // list of keys for Put , Delete Object tests.
        private List<string> keys;

        public BoltS3Perf()
        {
            s3Client = new AmazonS3Client();
            boltS3Client = new BoltS3Client();
        }

        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> ProcessEvent(Dictionary<string, string> input)
        {

            // If requestType is not passed, perform all perf tests.
            input.TryGetValue("requestType", out string requestTypeStr);
            requestType = string.IsNullOrEmpty(requestTypeStr) ? RequestType.ALL :
                (RequestType)Enum.Parse(typeof(RequestType), requestTypeStr.ToUpper());


            // update max no of keys and object data length, in passed in as input.
            input.TryGetValue("numKeys", out string numKeysStr);
            numKeys = string.IsNullOrEmpty(numKeysStr) ? 1000 : Int32.Parse(numKeysStr);
            if (numKeys > 1000)
            {
                numKeys = 1000;
            }

            input.TryGetValue("objLength", out string objLengthStr);
            objLength = string.IsNullOrEmpty(objLengthStr) ? 100 : Int32.Parse(objLengthStr);

            // If PUT, DELETE, ALL Object, generate key names.
            // If Get Object (including passthrough), list objects (up to numKeys) to get key names
            if (requestType == RequestType.PUT_OBJECT ||
                requestType == RequestType.DELETE_OBJECT ||
                requestType == RequestType.ALL)
            {
                keys = GenerateKeyNames(numKeys);
            } else if (requestType == RequestType.GET_OBJECT ||
                requestType == RequestType.GET_OBJECT_PASSTHROUGH ||
                requestType == RequestType.GET_OBJECT_TTFB ||
                requestType == RequestType.GET_OBJECT_PASSTHROUGH_TTFB)
            {
                keys = ListObjectsV2(input["bucket"]);
            }

            Dictionary<string, Dictionary<string, Dictionary<string, string>>> respDict;
            try
            {
                switch (requestType)
                {
                    case RequestType.LIST_OBJECTS_V2:
                        respDict = ListObjectsV2Perf(input["bucket"]);
                        break;
                    case RequestType.PUT_OBJECT:
                        respDict = PutObjectPerf(input["bucket"]);
                        break;
                    case RequestType.DELETE_OBJECT:
                        respDict = DeleteObjectPerf(input["bucket"]);
                        break;
                    case RequestType.GET_OBJECT:
                        respDict = GetObjectPerf(input["bucket"]);
                        break;
                    case RequestType.GET_OBJECT_TTFB:
                        respDict = GetObjectPerf(input["bucket"]);
                        break;
                    case RequestType.GET_OBJECT_PASSTHROUGH:
                        respDict = GetObjectPassthroughPerf(input["bucket"]);
                        break;
                    case RequestType.GET_OBJECT_PASSTHROUGH_TTFB:
                        respDict = GetObjectPassthroughPerf(input["bucket"]);
                        break;
                    case RequestType.ALL:
                        respDict = AllPerf(input["bucket"]);
                        break;
                    default:
                        respDict = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                        break;
                }
            }
            catch (AmazonS3Exception e)
            {
                Dictionary<string, string> errDict = new Dictionary<string, string>()
                {
                    {"errorMessage", e.Message},
                    {"errorCode", e.ErrorCode}
                };

                respDict = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
                {
                    {"error", new Dictionary<string, Dictionary<string, string>>(){ {"AmazonS3Exception", errDict } } }
                };
            }
            catch (Exception e)
            {
                Dictionary<string, string> errDict = new Dictionary<string, string>()
                {
                    {"errorMessage", e.Message}
                };

                respDict = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
                {
                    {"error", new Dictionary<string, Dictionary<string, string>>(){ {"Exception", errDict } } }
                };
            }
            return respDict;
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> ListObjectsV2Perf(string bucket, int numIter = 10)
        {
            List<long> s3ListObjTimes = new List<long>();
            List<long> boltListObjTimes = new List<long>();
            List<double> s3ListObjTp = new List<double>();
            List<double> boltListObjTp = new List<double>();

            ListObjectsV2Request listObjectsV2Request = new ListObjectsV2Request
            {
                BucketName = bucket,
                MaxKeys = 1000
            };
            Stopwatch stopwatch = new Stopwatch();

            // list 1000 objects from S3, num_iter times.
            for (int i = 1; i <= numIter; i++) {

                stopwatch.Restart();

                Task<ListObjectsV2Response> listObjV2Task =
                    s3Client.ListObjectsV2Async(listObjectsV2Request);
                ListObjectsV2Response listObjectsV2Response = listObjV2Task.Result;
                stopwatch.Stop();

                // calc latency
                long listObjV2Time = stopwatch.ElapsedMilliseconds;
                s3ListObjTimes.Add(listObjV2Time);

                //calc throughput
                double listObjV2Tp = (double)listObjectsV2Response.KeyCount / listObjV2Time;
                s3ListObjTp.Add(listObjV2Tp);
            }

            // list 1000 objects from Bolt, num_iter times.
            for (int i = 1; i <= numIter; i++)
            {

                stopwatch.Restart();

                Task<ListObjectsV2Response> listObjV2Task =
                    boltS3Client.ListObjectsV2Async(listObjectsV2Request);
                ListObjectsV2Response listObjectsV2Response = listObjV2Task.Result;
                stopwatch.Stop();

                // calc latency
                long listObjV2Time = stopwatch.ElapsedMilliseconds;
                boltListObjTimes.Add(listObjV2Time);

                //calc throughput
                double listObjV2Tp = (double)listObjectsV2Response.KeyCount / listObjV2Time;
                boltListObjTp.Add(listObjV2Tp);
            }

            // calc s3 perf stats.
            var s3ListObjPerfStats = ComputePerfStats(s3ListObjTimes, s3ListObjTp);

            // calc bolt perf stats.
            var boltListObjPerfStats = ComputePerfStats(boltListObjTimes, boltListObjTp);

            return new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
            {
                {"s3_list_objects_v2_perf_stats", s3ListObjPerfStats},
                {"bolt_list_objects_v2_perf_stats", boltListObjPerfStats}
            };
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> PutObjectPerf(string bucket)
        {
            List<long> s3PutObjTimes = new List<long>();
            List<long> boltPutObjTimes = new List<long>();

            Stopwatch stopwatch = new Stopwatch();

            // Upload objects to Bolt / S3.
            foreach (string key in keys)
            {
                string value = Generate(objLength);

                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    ContentBody = value
                };

                // upload object to S3.
                stopwatch.Restart();
                var putObjTask = s3Client.PutObjectAsync(putObjectRequest);
                putObjTask.Wait();
                stopwatch.Stop();

                // calc latency
                long putObjTime = stopwatch.ElapsedMilliseconds;
                s3PutObjTimes.Add(putObjTime);

                // upload object to Bolt.
                stopwatch.Restart();
                putObjTask = boltS3Client.PutObjectAsync(putObjectRequest);
                putObjTask.Wait();
                stopwatch.Stop();

                // calc latency
                putObjTime = stopwatch.ElapsedMilliseconds;
                boltPutObjTimes.Add(putObjTime);
            }

            // calc s3 perf stats.
            var s3PutObjPerfStats = ComputePerfStats(s3PutObjTimes);

            // calc bolt perf stats.
            var boltPutObjPerfStats = ComputePerfStats(boltPutObjTimes);

            return new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
            {
                {"s3_put_obj_perf_stats", s3PutObjPerfStats},
                {"bolt_put_obj_perf_stats", boltPutObjPerfStats}
            };
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> DeleteObjectPerf(string bucket)
        {
            List<long> s3DelObjTimes = new List<long>();
            List<long> boltDelObjTimes = new List<long>();

            Stopwatch stopwatch = new Stopwatch();

            // Delete objects from Bolt / S3.
            foreach (string key in keys)
            {
                var deleteObjectRequest = new DeleteObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };

                // Delete object from S3.
                stopwatch.Restart();
                var delObjTask = s3Client.DeleteObjectAsync(deleteObjectRequest);
                delObjTask.Wait();
                stopwatch.Stop();

                // calc latency
                long delObjTime = stopwatch.ElapsedMilliseconds;
                s3DelObjTimes.Add(delObjTime);

                // Delete object from Bolt.
                stopwatch.Restart();
                delObjTask = boltS3Client.DeleteObjectAsync(deleteObjectRequest);
                delObjTask.Wait();
                stopwatch.Stop();

                // calc latency
                delObjTime = stopwatch.ElapsedMilliseconds;
                boltDelObjTimes.Add(delObjTime);
            }

            // calc s3 perf stats.
            var s3DelObjPerfStats = ComputePerfStats(s3DelObjTimes);

            // calc bolt perf stats.
            var boltDelObjPerfStats = ComputePerfStats(boltDelObjTimes);

            return new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
            {
                {"s3_del_obj_perf_stats", s3DelObjPerfStats},
                {"bolt_del_obj_perf_stats", boltDelObjPerfStats}
            };
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> GetObjectPerf(string bucket)
        {
            List<long> s3GetObjTimes = new List<long>();
            List<long> boltGetObjTimes = new List<long>();

            List<long> s3ObjSizes = new List<long>();
            List<long> boltObjSizes = new List<long>();

            int s3CmpObjCount = 0;
            int s3UncmpObjCount = 0;
            int boltCmpObjCount = 0;
            int boltUncmpObjCount = 0;

            Stopwatch stopwatch = new Stopwatch();

            // Get Objects from S3.
            foreach (string key in keys)
            {
                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };

                stopwatch.Restart();
                var getObjectTask = s3Client.GetObjectAsync(getObjectRequest);

                using GetObjectResponse response = getObjectTask.Result;
                using Stream responseStream = response.ResponseStream;
                using StreamReader reader = new StreamReader(responseStream);

                // If getting first byte object latency, read at most 1 byte,
                // otherwise read the entire body.
                if (requestType == RequestType.GET_OBJECT_TTFB)
                {
                    // read only first byte from the stream.
                    reader.Read();
                }
                else
                {
                    // read all the data from the stream.
                    char[] readBuffer = new char[4096];
                    while (reader.Read(readBuffer, 0, readBuffer.Length) > 0) ;
                }
                stopwatch.Stop();

                // calc latency
                long getObjTime = stopwatch.ElapsedMilliseconds;
                s3GetObjTimes.Add(getObjTime);

                // count object
                string encoding = response.Headers.ContentEncoding;
                if ((!string.IsNullOrEmpty(encoding) &&
                    encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                    || key.EndsWith(".gz"))
                {
                    s3CmpObjCount++;
                }
                else
                {
                    s3UncmpObjCount++;
                }

                // get object size.
                s3ObjSizes.Add(response.ContentLength);
            }

            // Get Objects from Bolt.
            foreach (string key in keys)
            {
                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };

                stopwatch.Restart();
                var getObjectTask = boltS3Client.GetObjectAsync(getObjectRequest);

                using GetObjectResponse response = getObjectTask.Result;
                using Stream responseStream = response.ResponseStream;
                using StreamReader reader = new StreamReader(responseStream);

                // If getting first byte object latency, read at most 1 byte,
                // otherwise read the entire body.
                if (requestType == RequestType.GET_OBJECT_TTFB)
                {
                    // read only first byte from the stream.
                    reader.Read();
                }
                else
                {
                    // read all the data from the stream.
                    char[] readBuffer = new char[4096];
                    while (reader.Read(readBuffer, 0, readBuffer.Length) > 0) ;
                }
                stopwatch.Stop();

                // calc latency
                long getObjTime = stopwatch.ElapsedMilliseconds;
                boltGetObjTimes.Add(getObjTime);

                // count object
                string encoding = response.Headers.ContentEncoding;
                if ((!string.IsNullOrEmpty(encoding) &&
                    encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                    || key.EndsWith(".gz"))
                {
                    boltCmpObjCount++;
                }
                else
                {
                    boltUncmpObjCount++;
                }

                // get object size.
                boltObjSizes.Add(response.ContentLength);
            }

            // calc s3 perf stats.
            var s3GetObjPerfStats = ComputePerfStats(s3GetObjTimes, objSizes: s3ObjSizes);

            // calc bolt perf stats.
            var boltGetObjPerfStats = ComputePerfStats(boltGetObjTimes, objSizes: boltObjSizes);

            string s3GetObjStatName, boltGetObjStatName;
            if (requestType == RequestType.GET_OBJECT_TTFB)
            {
                s3GetObjStatName = "s3_get_obj_ttfb_perf_stats";
                boltGetObjStatName = "bolt_get_obj_ttfb_perf_stats";
            }
            else
            {
                s3GetObjStatName = "s3_get_obj_perf_stats";
                boltGetObjStatName = "bolt_get_obj_perf_stats";
            }

            Dictionary<string, string> s3Count = new Dictionary<string, string>()
            {
                {"compressed", s3CmpObjCount.ToString()},
                {"uncompressed", s3UncmpObjCount.ToString()}
            };

            Dictionary<string, string> boltCount = new Dictionary<string, string>()
            {
                {"compressed", boltCmpObjCount.ToString()},
                {"uncompressed", boltUncmpObjCount.ToString()}
            };

            var objCount = new Dictionary<string, Dictionary<string, string>>()
            {
                {"s3Count", s3Count},
                {"boltCount", boltCount}
            };

            return new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
            {
                {s3GetObjStatName, s3GetObjPerfStats},
                {boltGetObjStatName, boltGetObjPerfStats},
                {"object_count", objCount}
            };
        }
        
        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> GetObjectPassthroughPerf(string bucket)
        {
            List<long> boltGetObjTimes = new List<long>();

            List<long> boltObjSizes = new List<long>();

            int boltCmpObjCount = 0;
            int boltUncmpObjCount = 0;

            Stopwatch stopwatch = new Stopwatch();

            // Get objects via passthrough from Bolt.
            // Get Objects from Bolt.
            foreach (string key in keys)
            {
                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };

                stopwatch.Restart();
                var getObjectTask = boltS3Client.GetObjectAsync(getObjectRequest);

                using GetObjectResponse response = getObjectTask.Result;
                using Stream responseStream = response.ResponseStream;
                using StreamReader reader = new StreamReader(responseStream);

                // If getting first byte object latency, read at most 1 byte,
                // otherwise read the entire body.
                if (requestType == RequestType.GET_OBJECT_PASSTHROUGH_TTFB)
                {
                    // read only first byte from the stream.
                    reader.Read();
                }
                else
                {
                    // read all the data from the stream.
                    char[] readBuffer = new char[4096];
                    while (reader.Read(readBuffer, 0, readBuffer.Length) > 0) ;
                }
                stopwatch.Stop();

                // calc latency
                long getObjTime = stopwatch.ElapsedMilliseconds;
                boltGetObjTimes.Add(getObjTime);

                // count object
                string encoding = response.Headers.ContentEncoding;
                if ((!string.IsNullOrEmpty(encoding) &&
                    encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
                    || key.EndsWith(".gz"))
                {
                    boltCmpObjCount++;
                }
                else
                {
                    boltUncmpObjCount++;
                }

                // get object size.
                boltObjSizes.Add(response.ContentLength);
            }

            // calc bolt perf stats.
            var boltGetObjPtPerfStats = ComputePerfStats(boltGetObjTimes, objSizes: boltObjSizes);

            string boltGetObjPtStatName;
            if (requestType == RequestType.GET_OBJECT_PASSTHROUGH_TTFB)
            {
                boltGetObjPtStatName = "bolt_get_obj_pt_ttfb_perf_stats";
            }
            else
            {
                boltGetObjPtStatName = "bolt_get_obj_pt_perf_stats";
            }

            Dictionary<string, string> boltCount = new Dictionary<string, string>()
            {
                {"compressed", boltCmpObjCount.ToString()},
                {"uncompressed", boltUncmpObjCount.ToString()}
            };

            var objCount = new Dictionary<string, Dictionary<string, string>>()
            {
                {"boltCount", boltCount}
            };

            return new Dictionary<string, Dictionary<string, Dictionary<string, string>>>()
            {
                {boltGetObjPtStatName, boltGetObjPtPerfStats},
                {"object_count", objCount}
            };
        }
        
        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> AllPerf(string bucket)
        {
            // Put, Delete Object perf tests using generated key names.
            var putObjPerfStats = PutObjectPerf(bucket);
            var delObjPerfStats = DeleteObjectPerf(bucket);

            // List Objects perf tests on existing objects.
            var listObjsPerfStats = ListObjectsV2Perf(bucket);

            // Get the list of objects before get object perf test.
            keys = ListObjectsV2(bucket);
            var getObjPerfStats = GetObjectPerf(bucket);

            return MergePerfStats(putObjPerfStats, delObjPerfStats, listObjsPerfStats, getObjPerfStats);
        }

        private Dictionary<string, Dictionary<string, Dictionary<string, string>>> MergePerfStats(
            params Dictionary<string, Dictionary<string, Dictionary<string, string>>>[] perfStats)
        {
            var mergedPerfStats = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
            foreach (var perfStat in perfStats)
            {
                foreach(var stat in perfStat)
                {
                    mergedPerfStats.Add(stat.Key, stat.Value);
                }
            }
            return mergedPerfStats;
        }

        private string Generate(int objLength = 10)
        {
            const string src = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var sb = new StringBuilder();
            Random rand = new Random();

            for (int i = 0; i < objLength; i++)
            {
                var c = src[rand.Next(0, src.Length)];
                sb.Append(c);
            }
            return sb.ToString();
        }

        private List<string> GenerateKeyNames(int numObjects)
        {
            List<string> keys = new List<string>();

            for (int i = 0; i < numObjects; i++)
            {
                var keyBuilder = new StringBuilder();
                keyBuilder.Append("bolt-s3-perf");
                keyBuilder.Append(i);
                keys.Add(keyBuilder.ToString());
            }
            return keys;
        }

        private List<string> ListObjectsV2(string bucket)
        {
            List<string> keys = new List<string>();

            ListObjectsV2Request listObjectsV2Request = new ListObjectsV2Request
            {
                BucketName = bucket,
                MaxKeys = numKeys
            };

            var listObjectsV2Task = s3Client.ListObjectsV2Async(listObjectsV2Request);
            var response = listObjectsV2Task.Result;

            foreach (S3Object entry in response.S3Objects)
            {
                keys.Add(entry.Key);
            }
            return keys;
        }

        public Dictionary<string, Dictionary<string, string>> ComputePerfStats(
            List<long> opTimes,
            List<double> opTp = null,
            List<long> objSizes = null)
        {
            // calc op latency perf.
            double opAvgTime = opTimes.Average();
            opTimes.Sort();
            long opTimeP50 = opTimes[opTimes.Count / 2];
            int p90_index = (int)(opTimes.Count * 0.9);
            long opTimeP90 = opTimes[p90_index];

            Dictionary<string, string> latencyPerfStats = new Dictionary<string, string>()
            {
                {"average", string.Format("{0:0.00} ms", opAvgTime)},
                {"p50", string.Format("{0:0.00} ms", opTimeP50)},
                {"p90", string.Format("{0:0.00} ms", opTimeP90)}
            };

            // calc op throughput perf.
            Dictionary<string, string> TpPerfStats;
            if (opTp != null)
            {
                double opAvgTp = opTp.Average();
                opTp.Sort();
                double opTpP50 = opTp[opTp.Count / 2];
                p90_index = (int)(opTp.Count * 0.9);
                double opTpP90 = opTp[p90_index];

                TpPerfStats = new Dictionary<string, string>()
                {
                    {"average", string.Format("{0:0.00} objects/ms", opAvgTp)},
                    {"p50", string.Format("{0:0.00} objects/ms", opTpP50)},
                    {"p90", string.Format("{0:0.00} objects/ms", opTpP90)}
                };
            }
            else
            {
                double tp = (double)opTimes.Count / opTimes.Sum();
                TpPerfStats = new Dictionary<string, string>()
                {
                    {"throughput", string.Format("{0:0.00} objects/ms", tp) }
                };
            }

            // calc obj size metrics
            Dictionary<string, string> objSizesPerfStats = null;
            if (objSizes != null)
            {
                double objAvgSize = objSizes.Average();
                objSizes.Sort();
                long objSizesP50 = objSizes[objSizes.Count / 2];
                p90_index = (int)(objSizes.Count * 0.9);
                long objSizesP90 = objSizes[p90_index];

                objSizesPerfStats = new Dictionary<string, string>()
                {
                    {"average", string.Format("{0:0.00} bytes", objAvgSize)},
                    {"p50", string.Format("{0:0.00} bytes", objSizesP50)},
                    {"p90", string.Format("{0:0.00} bytes", objSizesP90)}
                };
            }

            Dictionary<string, Dictionary<string, string>> perfStats =
                new Dictionary<string, Dictionary<string, string>>()
                {
                    {"latency", latencyPerfStats},
                    {"throughput", TpPerfStats}
                };

            if (objSizes != null)
            {
                perfStats.Add("objectSize", objSizesPerfStats);
            }

            return perfStats;
        }
    }
}
