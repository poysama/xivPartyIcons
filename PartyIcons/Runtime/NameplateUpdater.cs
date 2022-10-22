﻿using System;
using System.Linq;
using Dalamud.Game.ClientState;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using PartyIcons.Api;
using PartyIcons.Entities;
using PartyIcons.Utils;
using PartyIcons.View;

namespace PartyIcons.Runtime;

public sealed class NameplateUpdater : IDisposable
{
    [PluginService]
    private ClientState ClientState { get; set; }

    private readonly NameplateView _view;
    private readonly PluginAddressResolver _address;
    private readonly ViewModeSetter _modeSetter;
    private readonly Hook<SetNamePlateDelegate> _hook;

    public int DebugIcon { get; set; } = -1;
    
    public NameplateUpdater(PluginAddressResolver address, NameplateView view, ViewModeSetter modeSetter)
    {
        _address = address;
        _view = view;
        _modeSetter = modeSetter;
        _hook = new Hook<SetNamePlateDelegate>(_address.AddonNamePlate_SetNamePlatePtr, SetNamePlateDetour);
    }

    public void Enable()
    {
        _hook.Enable();
    }
    
    public void Disable()
    {
        _hook.Disable();
    }

    public void Dispose()
    {
        Disable();
        _hook.Dispose();
    }

    public IntPtr SetNamePlateDetour(IntPtr namePlateObjectPtr, bool isPrefixTitle, bool displayTitle, IntPtr title,
        IntPtr name, IntPtr fcName, int iconID)
    {
        try
        {
            return SetNamePlate(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "SetNamePlateDetour encountered a critical error");

            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }
    }

    public IntPtr SetNamePlate(IntPtr namePlateObjectPtr, bool isPrefixTitle, bool displayTitle, IntPtr title,
        IntPtr name, IntPtr fcName, int iconID)
    {
        if (ClientState.IsPvP)
        {
            // disable in PvP
            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }

        var originalTitle = title;
        var originalName = name;
        var originalFcName = fcName;

        var npObject = new XivApi.SafeNamePlateObject(namePlateObjectPtr);

        if (npObject == null)
        {
            _view.SetupDefault(npObject);

            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }

        var npInfo = npObject.NamePlateInfo;

        if (npInfo == null)
        {
            _view.SetupDefault(npObject);

            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }

        var actorID = npInfo.Data.ObjectID.ObjectID;

        if (actorID == 0xE0000000)
        {
            _view.SetupDefault(npObject);

            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }

        if (!npObject.IsPlayer)
        {
            _view.SetupDefault(npObject);

            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }

        var jobID = npInfo.GetJobID();

        if (jobID < 1 || jobID >= Enum.GetValues(typeof(Job)).Length)
        {
            _view.SetupDefault(npObject);

            return _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        }
        
        var isPriorityIcon = IsPriorityIcon(iconID, out var priorityIconId);

        _view.NameplateDataForPC(npObject, ref isPrefixTitle, ref displayTitle, ref title, ref name, ref fcName, ref iconID);

        if (isPriorityIcon)
        {
            iconID = priorityIconId;
        }

        var result = _hook.Original(namePlateObjectPtr, isPrefixTitle, displayTitle, title, name, fcName, iconID);
        _view.SetupForPC(npObject, isPriorityIcon);

        if (originalName != name)
        {
            SeStringUtils.FreePtr(name);
        }

        if (originalTitle != title)
        {
            SeStringUtils.FreePtr(title);
        }

        if (originalFcName != fcName)
        {
            SeStringUtils.FreePtr(fcName);
        }

        return result;
    }

    /// <summary>
    /// Check for an icon that should take priority over the job icon,
    /// taking into account whether or not the player is in a duty.
    /// </summary>
    /// <param name="iconId">The incoming icon id that is being overwritten by the plugin.</param>
    /// <param name="priorityIconId">The icon id that should be used.</param>
    /// <returns>Whether a priority icon was found.</returns>
    private bool IsPriorityIcon(int iconId, out int priorityIconId)
    {
        // PluginLog.Debug($"Icon ID: {iconId}");
        
        // Select which set of priority icons to use based on whether we're in a duty
        // In the future, there can be a third list used when in combat
        var priorityIcons = _modeSetter.InDuty ? priorityIconsInDuty : priorityIconsOverworld;

        // Determine whether the incoming icon should take priority over the job icon 
        bool isPriorityIcon = priorityIcons.Contains(iconId);
        
        // Save the id of the icon
        priorityIconId = iconId;

        // If an icon was set with the plugin's debug command, always use that
        if (DebugIcon >= 0)
        {
            isPriorityIcon = true;
            priorityIconId = DebugIcon;
        }

        return isPriorityIcon;
    }
    
    private static readonly int[] priorityIconsOverworld =
    {
        061503, // Disconnecting
        061508, // Viewing Cutscene
        061509, // Busy
        061511, // Idle
        061517, // Duty Finder
        061521, // Party Leader
        061522, // Party Member
        061545, // Role Playing
    };

    private static readonly int[] priorityIconsInDuty =
    {
        061503, // Disconnecting
        061508, // Viewing Cutscene
        061511, // Idle
    };
}