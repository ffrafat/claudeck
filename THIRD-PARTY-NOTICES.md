# Third-party notices

ClauDeck is © 2026 Faisal F Rafat under the [MIT licence](LICENSE). It builds on the
work below, whose licences ask for their notices to travel with it.

## Projects ClauDeck learned from

### claude-graft

<https://github.com/aaditya-v-more/claude-graft>. The macOS app whose approach
ClauDeck follows on Windows: a separate Claude profile per account, the rules for
never touching data a profile owns, and reading each account's plan limits from
Anthropic with the access token the profile already holds (never the refresh token).

```
MIT License

Copyright (c) 2026 Aaditya Vitthal More

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Claude Profile Switcher

<https://github.com/PriyanshuGeTRekT/Claude-Code-Desktop-Switcher>. The Windows
profile launcher ClauDeck grew out of. Carried over from it: writing multi-size `.ico`
files from PNG frames (`Core/Icons.cs`), and telling Claude Desktop's own processes
apart from the Claude Code command line, which shares its exe name (`Core/Processes.cs`).

```
MIT License

Copyright (c) 2026 Priyanshu (PriyanshuGeTRekT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Libraries shipped with ClauDeck

| Component | Licence | Source |
|---|---|---|
| Windows App SDK and WinUI 3 | MIT | <https://github.com/microsoft/WindowsAppSDK>, <https://github.com/microsoft/microsoft-ui-xaml> |
| C#/WinRT | MIT | <https://github.com/microsoft/CsWinRT> |
| System.Drawing.Common | MIT | <https://github.com/dotnet/winforms> |
| .NET runtime (installed separately) | MIT | <https://github.com/dotnet/runtime> |

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) by Jordan
Russell and Martijn Laan, under the Inno Setup licence.

## Trademarks

ClauDeck is an independent project. It is not made, endorsed or supported by Anthropic.
Claude and the Claude logo are trademarks of Anthropic, PBC. ClauDeck does not include
Claude's artwork: account avatars are drawn at run time from the Claude app installed
on the user's own PC.
