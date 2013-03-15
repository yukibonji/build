﻿// Copyright 2013 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.

/// Utilities for working with Mercurial (Hg) repositories.
module IntelliFactory.Build.Mercurial

/// Given a Mercurial checkout (a directory containing a `.hg` subfolder),
/// infers the current Mercurial tag. If the current directory state is not
/// tagged, returns the long hash instead. Returns `None` if `.hg` is not found.
/// This method does not call Mercurial but analyzes the `.hg` folder directly.
val InferTag : folder: string -> option<string>