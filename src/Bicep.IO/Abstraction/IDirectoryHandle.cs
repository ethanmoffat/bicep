// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bicep.IO.Abstraction
{
    public interface IDirectoryHandle : IIOHandle
    {
        IDirectoryHandle EnsureExists();

        void Delete();

        IDirectoryHandle? GetParent();

        IDirectoryHandle GetDirectory(string relativePath);

        IFileHandle GetFile(string relativePath);

        IEnumerable<IDirectoryHandle> EnumerateDirectories(string searchPattern = "");

        IEnumerable<IFileHandle> EnumerateFiles(string searchPattern = "");

        /// <summary>
        /// Whether this directory is a symbolic link, junction or other reparse point.
        /// Callers that walk a directory tree use this to avoid following links out of the tree they are bounded to.
        /// </summary>
        bool IsSymbolicLink();
    }
}
