package sg.from.fbs.repository

abstract class BaseRepository<T> {
    abstract fun getList(): List<T>

    // We make these abstract so that implementing classes can define them and invoke getList(),
    // allowing the proxy to intercept getList() when called from external beans, but
    // internal calls to find/get within the same proxy bean will still call the unproxied getList().
    // Wait, if an external bean calls `repository.find()`, and `find` is just a method that calls `this.getList()`,
    // the internal `this.getList()` call will NOT go through the proxy!
    // To fix this, `getList()` must be called from the proxy. The easiest way is to NOT implement find/get in the base class/interface,
    // or inject a self-reference, or just have controllers call `repository.getList().find(...)`.
    // Let's implement them directly in the base class, but we shouldn't rely on `this.getList()`.
    // However, C# `IRepository` was an interface with no default methods, but let's check C# `FacilityRepository`.
    // In C#, `FindAsync` calls `GetListAsync(cancellationToken)`.
    // In Spring, we can just remove `IRepository` and implement `find` and `get` directly in each Repository, OR
    // we can use a Spring component that provides these methods.
    // For simplicity, let's just make getList() cached.
}

// Better yet, let's just inject self using @Lazy or we can just remove the base find/get implementation
// and implement them in the concrete class, using `getList()` which will be proxied ONLY IF called from OUTSIDE.
// Actually, if we implement `find` inside `UserRepository` which calls `getList()`, it won't be cached because it's an internal call!
// Therefore, the controllers MUST call `userRepository.getList().find { ... }` OR we inject the proxy into itself.
