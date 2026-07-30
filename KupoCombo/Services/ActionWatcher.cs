using System;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace KupoCombo.Services;

public unsafe sealed class ActionWatcher : IDisposable
{
    private delegate bool UseActionLocationDelegate(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        Vector3* location,
        uint extraParam,
        byte unknown);

    private readonly Hook<UseActionLocationDelegate> useActionLocationHook;

    public event Action<uint>? ActionUsed;

    public ActionWatcher(
        IGameInteropProvider gameInteropProvider)
    {
        useActionLocationHook =
            gameInteropProvider
                .HookFromAddress<UseActionLocationDelegate>(
                    ActionManager
                        .MemberFunctionPointers
                        .UseActionLocation,
                    UseActionLocationDetour);

        useActionLocationHook.Enable();
    }

    private bool UseActionLocationDetour(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        Vector3* location,
        uint extraParam,
        byte unknown)
    {
        var actionAccepted =
            useActionLocationHook.Original(
                actionManager,
                actionType,
                actionId,
                targetId,
                location,
                extraParam,
                unknown);

        try
        {
            if (actionAccepted &&
                actionType == ActionType.Action)
            {
                ActionUsed?.Invoke(actionId);
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.Error(
                exception,
                "Error while processing a detected action.");
        }

        return actionAccepted;
    }

    public void Dispose()
    {
        useActionLocationHook.Dispose();
    }
}
