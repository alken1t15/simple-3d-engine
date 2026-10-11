using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class GpuResourceCacheTests
{
    // Ключ с равенством по значению: кэш все равно должен различать экземпляры, как разные MeshData.
    private sealed record Key(int Id);

    private sealed class Resource(bool failOnDispose = false) : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            if (failOnDispose)
                throw new InvalidOperationException("dispose failed");
        }
    }

    [TestMethod]
    public void Shared_key_gets_one_resource()
    {
        int created = 0;
        var cache = new GpuResourceCache<Key, Resource>(_ => { created++; return new Resource(); });
        var key = new Key(1);

        var first = cache.GetOrCreate(key);
        for (int i = 0; i < 999; i++)
            Assert.AreSame(first, cache.GetOrCreate(key));

        Assert.AreEqual(1, created);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    public void Keys_are_compared_by_identity_not_by_value()
    {
        var cache = new GpuResourceCache<Key, Resource>(_ => new Resource());
        var first = new Key(1);
        var equalCopy = new Key(1);
        Assert.AreEqual(first, equalCopy);

        Assert.AreNotSame(cache.GetOrCreate(first), cache.GetOrCreate(equalCopy));
        Assert.AreEqual(2, cache.Count);
    }

    [TestMethod]
    public void DisposeAll_continues_after_a_failure_and_collects_errors()
    {
        var resources = new[] { new Resource(), new Resource(failOnDispose: true), new Resource() };
        var cache = new GpuResourceCache<Key, Resource>(key => resources[key.Id]);
        for (int i = 0; i < resources.Length; i++)
            cache.GetOrCreate(new Key(i));
        var errors = new List<Exception>();

        cache.DisposeAll(errors);

        Assert.ContainsSingle(errors);
        Assert.AreEqual("dispose failed", errors[0].Message);
        foreach (var resource in resources)
            Assert.AreEqual(1, resource.DisposeCalls);
        Assert.AreEqual(0, cache.Count);
    }
}
