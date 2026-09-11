module Fsxpired.Tests.Program

// The test host never runs this, but F# only initialises the top-level values of the last file in an
// executable from its entry point. Without one, the last test module's values would be null.
[<EntryPoint>]
let main _ = 0
