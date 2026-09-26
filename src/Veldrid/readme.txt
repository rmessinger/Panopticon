Copied from https://github.com/veldrid/veldrid at tag v4.9.0 (a121087cadf38755f28c397a5b3c42ff1c559a19), the source of the Veldrid 4.9.0 NuGet package.

Applied on top: upstream master commits c0135afa3 ("Add UniformRead to Vulkan memory barriers for uniform buffers") and afcd18eb0 ("Fix Vulkan validation issue with uniform shader memory barriers"). They are unreleased on NuGet. Without them, `CommandList.UpdateBuffer` on Vulkan makes transfer writes visible only to vertex-attribute reads, so shaders can read stale uniform data when a constant buffer is updated more than once in a command list.

Local changes are limited to those commits. Keep upstream formatting (see `.editorconfig`) so diffs against upstream stay reviewable.

Original code is licensed under MIT license:

The MIT License (MIT)

Copyright (c) 2017 Eric Mellino and Veldrid contributors

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