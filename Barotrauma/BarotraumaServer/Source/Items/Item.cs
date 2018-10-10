﻿using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            string errorMsg = "";
            if (extraData == null || extraData.Length == 0 || !(extraData[0] is NetEntityEvent.Type))
            {
                if (extraData == null)
                {
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - event data was null.";
                }
                else if (extraData.Length == 0)
                {
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - event data was empty.";
                }
                else
                {
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - event type not set.";
                }
                msg.WriteRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1, (int)NetEntityEvent.Type.Invalid);
                DebugConsole.Log(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.ServerWrite:InvalidData" + Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            int initialWritePos = msg.LengthBits;

            NetEntityEvent.Type eventType = (NetEntityEvent.Type)extraData[0];
            msg.WriteRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1, (int)eventType);
            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    if (extraData.Length < 2 || !(extraData[1] is int))
                    {
                        errorMsg = "Failed to write a component state event for the item \"" + Name + "\" - component index not given.";
                        break;
                    }
                    int componentIndex = (int)extraData[1];
                    if (componentIndex < 0 || componentIndex >= components.Count)
                    {
                        errorMsg = "Failed to write a component state event for the item \"" + Name + "\" - component index out of range (" + componentIndex + ").";
                        break;
                    }
                    else if (!(components[componentIndex] is IServerSerializable))
                    {
                        errorMsg = "Failed to write a component state event for the item \"" + Name + "\" - component \"" + components[componentIndex] + "\" is not server serializable.";
                        break;
                    }
                    msg.WriteRangedInteger(0, components.Count - 1, componentIndex);
                    (components[componentIndex] as IServerSerializable).ServerWrite(msg, c, extraData);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    if (extraData.Length < 2 || !(extraData[1] is int))
                    {
                        errorMsg = "Failed to write an inventory state event for the item \"" + Name + "\" - component index not given.";
                        break;
                    }
                    int containerIndex = (int)extraData[1];
                    if (containerIndex < 0 || containerIndex >= components.Count)
                    {
                        errorMsg = "Failed to write an inventory state event for the item \"" + Name + "\" - container index out of range (" + containerIndex + ").";
                        break;
                    }
                    else if (!(components[containerIndex] is ItemContainer))
                    {
                        errorMsg = "Failed to write an inventory state event for the item \"" + Name + "\" - component \"" + components[containerIndex] + "\" is not server serializable.";
                        break;
                    }
                    msg.WriteRangedInteger(0, components.Count - 1, containerIndex);
                    (components[containerIndex] as ItemContainer).Inventory.ServerWrite(msg, c);
                    break;
                case NetEntityEvent.Type.Status:
                    msg.Write(condition);
                    break;
                case NetEntityEvent.Type.ApplyStatusEffect:
                    ActionType actionType = (ActionType)extraData[1];
                    ushort targetID = extraData.Length > 2 ? (ushort)extraData[2] : (ushort)0;
                    Limb targetLimb = extraData.Length > 3 ? (Limb)extraData[3] : null;

                    Character targetCharacter = FindEntityByID(targetID) as Character;
                    byte targetLimbIndex = targetLimb != null && targetCharacter != null ? (byte)Array.IndexOf(targetCharacter.AnimController.Limbs, targetLimb) : (byte)255;

                    msg.WriteRangedInteger(0, Enum.GetValues(typeof(ActionType)).Length - 1, (int)actionType);
                    msg.Write(targetID);
                    msg.Write(targetLimbIndex);
                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    try
                    {
                        WritePropertyChange(msg, extraData, false);
                    }
                    catch (Exception e)
                    {
                        errorMsg = "Failed to write a ChangeProperty network event for the item \"" + Name + "\" (" + e.Message + ")";
                    }
                    break;
                default:
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - \"" + eventType + "\" is not a valid entity event type for items.";
                    break;
            }

            if (!string.IsNullOrEmpty(errorMsg))
            {
                //something went wrong - rewind the write position and write invalid event type to prevent creating an unreadable event
                msg.ReadBits(msg.Data, 0, initialWritePos);
                msg.LengthBits = initialWritePos;
                msg.WriteRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1, (int)NetEntityEvent.Type.Invalid);
                DebugConsole.Log(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.ServerWrite:" + errorMsg, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
            }

        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            NetEntityEvent.Type eventType =
                (NetEntityEvent.Type)msg.ReadRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);

            c.KickAFKTimer = 0.0f;

            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    int componentIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[componentIndex] as IClientSerializable).ServerRead(type, msg, c);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    int containerIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[containerIndex] as ItemContainer).Inventory.ServerRead(type, msg, c);
                    break;
                case NetEntityEvent.Type.ApplyStatusEffect:
                    if (c.Character == null || !c.Character.CanInteractWith(this)) return;

                    UInt16 characterID = msg.ReadUInt16();
                    byte limbIndex = msg.ReadByte();

                    Character targetCharacter = FindEntityByID(characterID) as Character;
                    if (targetCharacter == null) break;
                    if (targetCharacter != c.Character && c.Character.SelectedCharacter != targetCharacter) break;

                    Limb targetLimb = limbIndex < targetCharacter.AnimController.Limbs.Length ? targetCharacter.AnimController.Limbs[limbIndex] : null;
                    ApplyStatusEffects(ActionType.OnUse, 1.0f, targetCharacter, targetLimb);

                    if (ContainedItems == null || ContainedItems.All(i => i == null))
                    {
                        GameServer.Log(c.Character.LogName + " used item " + Name, ServerLog.MessageType.ItemInteraction);
                    }
                    else
                    {
                        GameServer.Log(
                            c.Character.LogName + " used item " + Name + " (contained items: " + string.Join(", ", Array.FindAll(ContainedItems, i => i != null).Select(i => i.Name)) + ")",
                            ServerLog.MessageType.ItemInteraction);
                    }

                    GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, c.Character.ID });

                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    ReadPropertyChange(msg, true);
                    break;
            }
        }

        public void WriteSpawnData(NetBuffer msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(Prefab.Name);
            msg.Write(Prefab.Identifier);
            msg.Write(Description != prefab.Description);
            if (Description != prefab.Description)
            {
                msg.Write(Description);
            }

            msg.Write(ID);

            if (ParentInventory == null || ParentInventory.Owner == null)
            {
                msg.Write((ushort)0);

                msg.Write(Position.X);
                msg.Write(Position.Y);
                msg.Write(Submarine != null ? Submarine.ID : (ushort)0);
            }
            else
            {
                msg.Write(ParentInventory.Owner.ID);

                //find the index of the ItemContainer this item is inside to get the item to
                //spawn in the correct inventory in multi-inventory items like fabricators
                byte containerIndex = 0;
                if (Container != null)
                {
                    for (int i = 0; i < Container.components.Count; i++)
                    {
                        if (Container.components[i] is ItemContainer container &&
                            container.Inventory == ParentInventory)
                        {
                            containerIndex = (byte)i;
                            break;
                        }
                    }
                }
                msg.Write(containerIndex);

                int slotIndex = ParentInventory.FindIndex(this);
                msg.Write(slotIndex < 0 ? (byte)255 : (byte)slotIndex);
            }

            byte teamID = 0;
            foreach (WifiComponent wifiComponent in GetComponents<WifiComponent>())
            {
                teamID = wifiComponent.TeamID;
                break;
            }

            msg.Write(teamID);
            bool tagsChanged = tags.Count != prefab.Tags.Count || !tags.All(t => prefab.Tags.Contains(t));
            msg.Write(tagsChanged);
            if (tagsChanged)
            {
                msg.Write(Tags);
            }

        }

        partial void UpdateNetPosition()
        {
            if (parentInventory != null) return;

            if (prevBodyAwake != body.FarseerBody.Awake || Vector2.Distance(lastSentPos, SimPosition) > NetConfig.ItemPosUpdateDistance)
            {
                needsPositionUpdate = true;
            }

            prevBodyAwake = body.FarseerBody.Awake;
        }

        public void CreateServerEvent<T>(T ic) where T : ItemComponent, IServerSerializable
        {
            if (GameMain.Server == null) return;

            int index = components.IndexOf(ic);
            if (index == -1) return;

            GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ComponentState, index });
        }
    }
}