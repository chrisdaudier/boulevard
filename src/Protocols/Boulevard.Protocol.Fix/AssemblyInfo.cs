using System.Runtime.CompilerServices;

// The internal helpers (FixValueParser, FixChecksum, FixTags, FixVersion) have their own edge
// cases worth testing directly rather than only indirectly through the public message TryParse
// surface.
[assembly: InternalsVisibleTo("Boulevard.Protocol.Fix.Tests")]
