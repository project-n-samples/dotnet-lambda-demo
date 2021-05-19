using System.Collections.Generic;
using Amazon.Lambda.Core;

namespace MPLambdaPOC
{
    public class BoltS3PerfHandler
    {
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> HandleRequest(Dictionary<string, string> input, ILambdaContext context)
        {
            BoltS3Perf boltS3Perf = new BoltS3Perf();
            return boltS3Perf.ProcessEvent(input);
        }
    }
}
