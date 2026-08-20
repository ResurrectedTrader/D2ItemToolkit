using System.Runtime.CompilerServices;

// The public surface is deliberately small — the DTO, the engine, and the result types. Everything
// that models the disassembly is internal, because naming it publicly would freeze a shape that
// exists to mirror the game rather than to be consumed.
//
// These four still need to reach inside. The test suite asserts on the traced units themselves, and
// Reference/Corpus generate the differential corpus LAYER BY LAYER (views, kind, sections, lines) —
// which is the whole point of it: a mismatch names the layer that broke. Going through the facade
// would collapse those layers into one string and lose that.
[assembly: InternalsVisibleTo("D2ItemToolkit.Tests")]
[assembly: InternalsVisibleTo("Reference")]
[assembly: InternalsVisibleTo("Corpus")]
[assembly: InternalsVisibleTo("RecordSmoke")]
[assembly: InternalsVisibleTo("DataSmoke")]
