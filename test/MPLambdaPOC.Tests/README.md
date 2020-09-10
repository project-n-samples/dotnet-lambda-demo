## Lambda integration tests

Expected setup:
 - Data cruncher running locally at port 9000 with `make local`.
 - Data cruncher has crunched `s3://in` with `make crunch_local`.
 - Data cruncher has received an ACL for the current user's ARN (returned via `aws sts get-caller-identity`) like so:
   ```bash
   ./ncli/bin/ncli -f s3://in put-policy --admins arn:aws:iam::...
   ```

Run tests like so:
```bash
BOLT_URL=http://localhost:9000 dotnet test
```