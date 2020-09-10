using Xunit;
using Amazon.Lambda.TestUtilities;

namespace MPLambdaPOC.Tests
{
    public class FunctionTest
    {
        [Fact]
        public async void TestToUpperFunction()
        {
            var function = new Function();
            Assert.True(await function.ListBucket("in", new TestLambdaContext()));
            Assert.False(await function.ListBucket("does-not-exist", new TestLambdaContext()));
        }
    }
}
