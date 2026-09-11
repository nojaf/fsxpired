#if INTERACTIVE
#r "nuget: OnlyInteractive, 2.0.0"
#endif
#if FOO
#r "nuget: OnlyFoo, 2.0.0"
#endif
module Inner =
    #r "nuget: Nested, 3.0.0"
    let x = 1
