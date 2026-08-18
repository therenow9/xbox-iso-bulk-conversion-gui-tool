# Third-party notices

XISO Converter is a front-end. It relies on, and in some builds redistributes,
software written by other people. This file records what, and under what terms.

---

## extract-xiso

Does all of the actual XISO extraction. This project would have nothing to do
without it.

- Project: https://github.com/XboxDev/extract-xiso
- Latest upstream release: https://github.com/XboxDev/extract-xiso/releases/latest
- Original author: *in* &lt;in@fishtank.com&gt;
- Maintained by: the XboxDev project
- Licence: 4-clause BSD (see `extract-xiso/LICENSE.TXT` in the release drop, or
  `third-party/extract-xiso/LICENSE.TXT` in the source tree)

> This product includes software developed by in &lt;in@fishtank.com&gt;.

Releases marked *bundled* ship an unmodified `extract-xiso.exe` from an official
XboxDev release, together with its `LICENSE.TXT` and a `VERSION.txt` naming the exact
upstream build. The bundled copy is the newest release available at the time that
version was built; the build warns if the pinned version has since fallen behind, and
the release notes only claim "latest" when a live check against the upstream API
confirms it. Releases not marked bundled do not redistribute it at all; you supply
your own copy.

Neither the name *in* nor the XboxDev project endorses this software.

```
Copyright (c) 2003 in <in@fishtank.com>
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

1. Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

3. All advertising materials mentioning features or use of this software
   must display the following acknowledgement:

   This product includes software developed by in <in@fishtank.com>.

4. Neither the name of "in" nor the email address "in@fishtank.com"
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED `AS IS' AND ANY EXPRESS OR IMPLIED WARRANTIES
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE
AUTHOR OR ANY CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS;
OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

## .NET and Windows Forms

The application is built on the .NET 8 runtime and Windows Forms, and self-contained
release builds redistribute the runtime inside `XisoConverter.exe`.

- https://github.com/dotnet/runtime
- https://github.com/dotnet/winforms
- Licence: MIT, Copyright (c) .NET Foundation and Contributors

---

## With thanks to

- **[Redump](http://redump.org)** — disc preservation work, and the source of the
  full-dump disc layout that the media-signature check recognises.
- The wider **Original Xbox homebrew community**, whose documentation of the disc
  format and of FATX's naming limits is what makes the naming rules possible.
