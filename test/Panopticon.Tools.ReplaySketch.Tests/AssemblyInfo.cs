using Xunit;

// Tests in this assembly boot full Game instances against a real install; several engine
// subsystems (translation manager, content caches) are process-wide singletons. Serialize
// test classes so concurrent boots cannot race them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
