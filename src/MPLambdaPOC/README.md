# MP proof-of-concept .NET Lambda

Link `bolt-sdk-net` for Bolt before running by running `make link` in `MPLambdaPOC/src/MPLambdaPOC`.

See `README.md` in the directory referenced by that command for details.

Install Amazon.Lambda.Tools Global Tools if not already installed.
```
    dotnet tool install -g Amazon.Lambda.Tools
```

If already installed check if new version is available.
```
    dotnet tool update -g Amazon.Lambda.Tools
```

Execute unit tests
```
    cd "MPLambdaPOC/test/MPLambdaPOC.Tests"
    dotnet test
```

Deploy function to AWS Lambda
```
    cd "MPLambdaPOC/src/MPLambdaPOC"
    dotnet lambda deploy-function
```
