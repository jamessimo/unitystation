﻿using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Morgue component for morgue objects. Adds additional function to the base Drawer component.
/// TODO: Consider using hacking to replace/complement the screwdriver and emag.
/// TODO: Add spark VFX with emag interaction.
/// </summary>
public class Morgue : Drawer
{
	// Extra states over the base DrawerState enum.
    private enum MorgueState
	{
		/// <summary> Yellow morgue lights. </summary>
		ShutWithItems = 3,
		/// <summary> Green morgue lights. </summary>
		ShutWithBraindead = 4,
		/// <summary> Red morgue lights. </summary>
		ShutWithPlayer = 2
	}

	[SerializeField]
	[Tooltip("Whether the morgue alarm should sound if a consciousness is present.")]
	private bool alarmBuzzerSystemEnabled = true;
	[SerializeField]
	[Tooltip("Whether the morgue alarm can be toggled. The LED display will still show red if a consciousness is present.")]
	private bool allowBuzzerSystemToggling = true;
	[SerializeField]
	[Tooltip("Whether the morgue can be emagged (permanently breaks the display and alarm - useful for hiding corpses in plain sight).")]
	private bool allowEmagging = true;

	private const int ALARM_PERIOD = 5; // In seconds.

	private bool consciousnessPresent = false;
	private bool buzzerEnabled;
	private bool alarmBroken = false;
	private bool alarmRunning = false;

	#region Init Methods

	protected void Start()
	{
		buzzerEnabled = alarmBuzzerSystemEnabled;
	}

	#endregion Init Methods

	#region Interactions

	public override bool WillInteract(HandApply interaction, NetworkSide side)
	{
		if (!DefaultWillInteract.Default(interaction, side)) return false;

		if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Screwdriver)) return true;
		if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Emag)) return true;
		if (interaction.HandObject == null) return true;

		return false;
	}

	public override void ServerPerformInteraction(HandApply interaction)
	{
		if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Screwdriver)) UseScrewdriver(interaction);
		else if (Validations.HasItemTrait(interaction.HandObject, CommonTraits.Instance.Emag)) UseEmag(interaction);
		else if (drawerState == DrawerState.Open) CloseDrawer();
		else OpenDrawer();
	}

	#endregion Interactions

	#region Server Only

	protected override void CloseDrawer()
	{
		base.CloseDrawer();
		// Note: the sprite setting done in base.CloseDrawer() would be overridden (an unnecessary sprite call).
		// "Not great, not terrible."

		UpdateCloseState();
	}

	protected override void EjectPlayers(bool morgueDespawning = false)
	{
		base.EjectPlayers(morgueDespawning);
		UpdateConsciousnessPresent();
	}

	private void UpdateConsciousnessPresent()
	{
		foreach (ObjectBehaviour player in serverHeldPlayers)
		{
			if (Conscious(player))
			{
				consciousnessPresent = true;
				return;
			}
		}

		consciousnessPresent = false;
	}

	private bool Conscious(ObjectBehaviour playerMob)
	{
		var playerMind = playerMob.GetComponent<PlayerScript>().mind;
		var playerMobID = playerMob.GetComponent<LivingHealthBehaviour>().mobID;

		// If the mob IDs do not match, player is controlling a new mob, so we don't care about this old mob.
		if (playerMind.bodyMobID == playerMobID && playerMind.IsOnline()) return true;

		return false;
	}

	private void UseScrewdriver(HandApply interaction)
	{
		if (!allowBuzzerSystemToggling) return;
		if (!alarmBuzzerSystemEnabled) return;

		ToolUtils.ServerUseToolWithActionMessages(interaction, 1f,
				$"You poke the {interaction.HandObject.name.ToLower()} around in the {name.ToLower()}'s electrical panel...",
				$"{interaction.Performer.ExpensiveName()} pokes a {interaction.HandObject.name.ToLower()} into the {name.ToLower()}'s electrical panel...",
				$"You manage to toggle {(!buzzerEnabled ? "on" : "off")} the consciousness-alerting buzzer.",
				$"{interaction.Performer.ExpensiveName()} removes the {interaction.HandObject.name.ToLower()}.",
				() => ToggleBuzzer());
	}

	private void UseEmag(HandApply interaction)
	{
		if (!allowEmagging) return;
		if (!alarmBuzzerSystemEnabled) return;
		if (alarmBroken) return;
		alarmBroken = true;

		Chat.AddActionMsgToChat(interaction,
				$"You wave the {interaction.HandObject.name.ToLower()} over the {name.ToLower()}'s electrical panel. " +
				"The status panel flickers and the buzzer makes sickly popping noises. You can smell smoke...",
				"You can smell caustic smoke from somewhere...");
		SoundManager.PlayNetworkedAtPos("SnapCracklePop1", DrawerWorldPosition, sourceObj: gameObject);
		StartCoroutine(PlayEmagAnimation());
	}

	private void ToggleBuzzer()
	{
		buzzerEnabled = !buzzerEnabled;
		SoundManager.PlayNetworkedAtPos("Pop", DrawerWorldPosition, sourceObj: gameObject);
		StartCoroutine(PlayAlarm());
	}

	private void UpdateCloseState()
	{
		UpdateConsciousnessPresent();

		if (consciousnessPresent && !alarmBroken)
		{
			drawerState = (DrawerState)MorgueState.ShutWithPlayer;
			StartCoroutine(PlayAlarm());
		}
		else if (serverHeldPlayers.Count > 0) drawerState = (DrawerState)MorgueState.ShutWithBraindead;
		else if (serverHeldItems.Count > 0) drawerState = (DrawerState)MorgueState.ShutWithItems;
		else drawerState = DrawerState.Shut;
	}

	private IEnumerator PlayAlarm()
	{
		if (!alarmBuzzerSystemEnabled || alarmRunning) yield break;

		alarmRunning = true;
		while (consciousnessPresent && buzzerEnabled && !alarmBroken)
		{
			SoundManager.PlayNetworkedAtPos("OutOfAmmoAlarm", DrawerWorldPosition, sourceObj: gameObject);
			yield return WaitFor.Seconds(ALARM_PERIOD);
			if (drawerState == DrawerState.Open) break;
			UpdateCloseState();
		}
		alarmRunning = false;
	}

	private IEnumerator PlayEmagAnimation(float stateDelay = 0.10f)
	{
		var oldState = drawerState;

		drawerState = (DrawerState)MorgueState.ShutWithItems;
		yield return WaitFor.Seconds(stateDelay);
		drawerState = (DrawerState)MorgueState.ShutWithPlayer;
		yield return WaitFor.Seconds(stateDelay);
		drawerState = (DrawerState)MorgueState.ShutWithItems;
		yield return WaitFor.Seconds(stateDelay);
		drawerState = (DrawerState)MorgueState.ShutWithPlayer;
		yield return WaitFor.Seconds(stateDelay);
		drawerState = (DrawerState)MorgueState.ShutWithBraindead;
		yield return WaitFor.Seconds(stateDelay * 6);

		if (oldState != DrawerState.Open) UpdateCloseState();
		else drawerState = DrawerState.Open;
	}

	#endregion Server Only
}
