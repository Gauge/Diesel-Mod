using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace Consumption
{

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Display : MySessionComponentBase
    {
        private bool JustGotInCockpit = true;
        private static readonly List<string> FuelStorage = new List<string>() { "V8Engine", "V8EngineTurbo", "SB1x3x3cargo", "SB1x5x5cargo", "hmindustrialtank_Large" };
        private List<IMySlimBlock> slims = new List<IMySlimBlock>();

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Session == null) return;

            IMyCockpit cockpit = MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity as IMyCockpit;
            if (cockpit == null)
            {
                JustGotInCockpit = true;
                return;
            }

            if (JustGotInCockpit)
            {
                JustGotInCockpit = false;
                slims.Clear();
                cockpit.CubeGrid.GetBlocks(slims, b => b.FatBlock != null && !b.FatBlock.BlockDefinition.IsNull() && FuelStorage.Contains(b.FatBlock.BlockDefinition.SubtypeId));
            }

            int blockCount = 0;
            double consumptionRate = 0;
            MyFixedPoint total = 0;
            MyFixedPoint current = 0;

            foreach (IMySlimBlock slim in slims)
            {
                IMyCubeBlock block = slim.FatBlock;
                IMyReactor reactor = block as IMyReactor;
                IMyInventory inv = block.GetInventory(0);
                if (!block.IsFunctional || !block.IsWorking || inv == null) continue;

                switch (block.BlockDefinition.SubtypeId)
                {
                    case "V8Engine":
                        consumptionRate += 0.06666666666666666666666666666667d * (reactor.CurrentOutput / reactor.MaxOutput);
                        break;
                    case "V8EngineTurbo":
                        consumptionRate += 0.11666666666666666666666666666667d * (reactor.CurrentOutput / reactor.MaxOutput);
                        break;
                    case "hmdieselgenerator3_Large":
                        consumptionRate += 0.05d * (reactor.CurrentOutput / reactor.MaxOutput);
                        break;
                    case "hmdieselgenerator1_Large":
                        consumptionRate += 0.00833333333333333333333333333333d * (reactor.CurrentOutput / reactor.MaxOutput);
                        break;
                }

                blockCount++;
                total += inv.MaxVolume;
                current += inv.CurrentVolume;
            }

            if (total > 0 && consumptionRate > 0)
            {
                double percent = (((double)current * 1000d) / ((double)total * 1000d) * 100d);
                double tick = (((double)current * 1000d) / consumptionRate);

                MyAPIGateway.Utilities.ShowNotification($"Fuel: {percent.ToString("n0")}% {TimeSpan.FromMilliseconds((tick / 60d) * 1000d).ToString("g").Split('.')[0]}", 1);
            }
        }
    }

    public class Consumption : MyGameLogicComponent
    {
        public static MyObjectBuilder_PhysicalObject PhysicalFuelObject = null;
        public static MyDefinitionId DefinitionId;
        public static readonly List<string> FuelStorage = new List<string>() { "SB1x3x3cargo", "SB1x5x5cargo", "hmindustrialtank_Large" };
        public double MaxConsumptionPerTick = 0;

        private IMyReactor ModBlock;
        private IMyInventory Inventory;

        private MyFixedPoint EmptySpace => ((Inventory.MaxVolume - Inventory.CurrentVolume) * 1000) - 1;

        private int fuelCheckThreshold = 10;

        private bool refreshInventory = true;
        private List<IMyInventory> fuelTanks = new List<IMyInventory>();

        // this is a hack cause keen doesn't have a better getblocks method.
        List<IMySlimBlock> temp = new List<IMySlimBlock>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            ModBlock = Entity as IMyReactor;

            MaxConsumptionPerTick = (MaxConsumptionPerTick / 60f) - (1 / 60 / 60 / 60 * ModBlock.MaxOutput);

            if (PhysicalFuelObject == null)
            {
                PhysicalFuelObject = new MyObjectBuilder_PhysicalObject();
                PhysicalFuelObject = new MyObjectBuilder_Ingot() { SubtypeName = "Diesel" };
                DefinitionId = new MyDefinitionId(PhysicalFuelObject.TypeId, PhysicalFuelObject.SubtypeId);
            }

            ModBlock.CubeGrid.OnBlockAdded += BlockAdded;

            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME;
        }

        private void BlockAdded(IMySlimBlock block)
        {
            refreshInventory = true;
        }

        public override void Close()
        {
            ModBlock.CubeGrid.OnBlockAdded -= BlockAdded;
        }

        public override void UpdateOnceBeforeFrame()
        {
            Inventory = ModBlock.GetInventory(0);
        }

        public override void UpdateBeforeSimulation()
        {
            if (!MyAPIGateway.Multiplayer.IsServer || 
                PhysicalFuelObject == null || 
                ModBlock.CurrentOutput == 0 || 
                !ModBlock.IsFunctional || 
                !ModBlock.IsWorking) return;

            float powerRatio = ModBlock.CurrentOutput / ModBlock.MaxOutput;
            double consumptionAmount = MaxConsumptionPerTick * powerRatio;


            MyFixedPoint quanity = Inventory.GetItemAmount(DefinitionId);

            // refill fuel tanks if they are low because keens pulling system for reactors is extremely slow.
            if (quanity < fuelCheckThreshold && quanity != 0)
            {
                // refreshes the tank inventories if new blocks have been added.
                if (refreshInventory)
                {
                    fuelTanks.Clear();
                    ModBlock.CubeGrid.GetBlocks(temp, (b) =>
                    {
                        if (b.FatBlock != null && !b.FatBlock.BlockDefinition.IsNull() && FuelStorage.Contains(b.FatBlock.BlockDefinition.SubtypeId))
                        {
                            fuelTanks.Add(b.FatBlock.GetInventory(0));
                        }
                        return false;
                    });

                    refreshInventory = false;
                }

                MyFixedPoint emptySpace = EmptySpace; // no reason to run calculations more than once

                foreach (IMyInventory inv in fuelTanks)
                {
                    if (inv == null) continue;
                    if (!Inventory.IsConnectedTo(inv)) continue;

                    MyFixedPoint value = inv.GetItemAmount(DefinitionId);

                    if (value == null) continue;

                    if (value > emptySpace)
                    {
                        inv.RemoveItemsOfType(emptySpace, PhysicalFuelObject, false);
                        Inventory.AddItems(emptySpace, PhysicalFuelObject);
                        break;
                    }
                    else if (value != 0)
                    {
                        inv.RemoveItemsOfType(value, PhysicalFuelObject, false);
                        Inventory.AddItems(value, PhysicalFuelObject);
                        break;
                    }
                }
            }

            if (quanity > (MyFixedPoint)consumptionAmount)
            {
                Inventory.RemoveItemsOfType((MyFixedPoint)consumptionAmount, PhysicalFuelObject, false);
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "V8Engine")]
    public class V8Engine : Consumption
    {
        public V8Engine()
        {
            MaxConsumptionPerTick = 4d;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "V8EngineTurbo")]
    public class V8EngineTurbo : Consumption
    {
        public V8EngineTurbo()
        {
            MaxConsumptionPerTick = 7d;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "hmdieselgenerator3_Large")]
    public class hmdieselgenerator3_Large : Consumption
    {
        public hmdieselgenerator3_Large()
        {
            MaxConsumptionPerTick = 3d;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "hmdieselgenerator1_Large")]
    public class hmdieselgenerator1_Large : Consumption
    {
        public hmdieselgenerator1_Large()
        {
            MaxConsumptionPerTick = 0.5d;
        }
    }

    public class InventoryConstraint : MyGameLogicComponent
    {
        public static MyObjectBuilder_PhysicalObject PhysicalFuelObject = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (PhysicalFuelObject == null)
            {
                PhysicalFuelObject = new MyObjectBuilder_PhysicalObject();
                PhysicalFuelObject = new MyObjectBuilder_Ingot() { SubtypeName = "Diesel" };
            }

            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            MyInventory inv = (MyInventory)Entity.GetInventory(0);

            if (inv == null) return;

            if (inv.Constraint == null)
            {
                MyInventoryConstraint constraint = new MyInventoryConstraint("Inventory: Diesel Not Allowed", null, false);
                constraint.Add(new MyDefinitionId(PhysicalFuelObject.TypeId, PhysicalFuelObject.SubtypeId));

                inv.Constraint = constraint;
            }
            else if (inv.Constraint.IsWhitelist == false)
            {
                inv.Constraint.Add(new MyDefinitionId(PhysicalFuelObject.TypeId, PhysicalFuelObject.SubtypeId));
            }

            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Cockpit), false)]
    public class Cockpits : InventoryConstraint
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipConnector), false)]
    public class ConveyorConnector : InventoryConstraint
    {
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CargoContainer), false)]
    public class Cargo : InventoryConstraint
    {
    }

    //[MyEntityComponentDescriptor(typeof(MyObjectBuilder_ConveyorSorter), false)]
    //public class Sorter : InventoryConstraint
    //{
    //}
}