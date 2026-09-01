# HttpMultipart

Source only package for reading and writing HTTP multipart content.

The reader is vendored from `Microsoft.AspNetCore.WebUtilities` and stripped of its dependencies, so it
works anywhere a `Stream` does — a Blazor WebAssembly client, a console app, a library that does not
want `Microsoft.Extensions.Primitives` and `Microsoft.Net.Http.Headers` in its dependency graph.

Requires `net10.0` or later.


## Usage

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="HttpMultipart" Version="*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

```csharp
if (response.Content.TryGetMultipartBoundary(out var boundary))
{
    await using var body = await response.Content.ReadAsStreamAsync();
    var reader = new MultipartReader(boundary, body);
    while (await reader.ReadNextSectionAsync() is {} section)
    {
        var text = await section.ReadAsStringAsync();
    }
}
```

The types live in the `HttpMultipart` namespace, and the package adds a global `using` for it wherever
the consuming project has implicit usings enabled.


## Key features

* **Source only** — compiles into the consuming project. No runtime dependency, nothing to deploy, and
  the types stay `internal` to whoever compiled them.
* **No package dependencies** — not even `Microsoft.Extensions.Primitives`.
* **Reader and writer** — `MultipartReader` and `MultipartWriter`, tested against each other.
* **Behaviourally identical to aspnetcore** — the upstream test suite passes against it unchanged.


## Documentation

See the [full documentation on GitHub](https://github.com/SimonCropp/HttpMultipart).
