# Compatibility shim for existing macros.
#
# Upstream ClassicAssist puts its engine in the CLR namespace `Assistant`, so macros - and the macro
# help shipped with this build - are written as:
#
#     from Assistant import Engine
#     Engine.Player.Name
#
# Here it lives in `ClassicAssist.Shared` instead. A module of this name on the macro search path
# means those macros keep working unchanged rather than every one of them needing an edit.
#
# `Engine` is the only public type in upstream's `Assistant` namespace, so it is the only thing that
# needs re-exporting.

from ClassicAssist.Shared import Engine

__all__ = ["Engine"]
