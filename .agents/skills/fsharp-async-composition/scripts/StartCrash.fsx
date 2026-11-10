Async.Start(async { return failwith "unhandled in Async.Start" })
System.Threading.Thread.Sleep 2000
System.Console.WriteLine "process survived"
