// The tests only read the fixtures and parse their own documents: they can run in parallel.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
