// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;

namespace Microsoft.DotNet.Tools.CrossGen.Exceptions
{
    public class CrossGenException : Exception
    {
        public CrossGenException(string msg, Exception cause = null)
            : base(msg, cause) { }
    }
}