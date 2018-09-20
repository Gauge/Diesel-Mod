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
        public static readonly List<string> FuelStorage = new List<string>() { "V8Engine", "V8EngineTurbo", "SB1x3x3cargo", "SB1x5x5cargo", "hmindustrialtank_Large" };
        private List<IMySlimBlock> slims = new List<IMySlimBlock>();
        int countdown = 0;

        protected override void UnloadData()
        {
        }

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (MyAPIGateway.Session == null) return;

            IMyCockpit cockpit = MyAPIGateway.Session.Player?.Controller?.ControlledEntity?.Entity as IMyCockpit;
            if (cockpit == null) return;

            if ((countdown--) <= 0)
            {
                countdown = 300;
                slims.Clear();
                cockpit.CubeGrid.GetBlocks(slims, b => b.FatBlock != null && !b.FatBlock.BlockDefinition.IsNull() && FuelStorage.Contains(b.FatBlock.BlockDefinition.SubtypeId));
            }

            int blockCount = 0;
            double consumptionRate = 0;
            MyFixedPoint total = 0;
            MyFixedPoint current = 0;

            foreach (IMySlimBlock slim in slims)
            {
                if (!slim.FatBlock.IsFunctional) continue;

                if (slim.FatBlock.BlockDefinition.SubtypeId == "V8Engine")
                {
                    IMyReactor reactor = slim.FatBlock as IMyReactor;
                    consumptionRate += 0.06666666666666666666666666666667d * (reactor.CurrentOutput / reactor.MaxOutput);
                }
                else if (slim.FatBlock.BlockDefinition.SubtypeId == "V8EngineTurbo")
                {
                    IMyReactor reactor = slim.FatBlock as IMyReactor;
                    consumptionRate += 0.11666666666666666666666666666667d * (reactor.CurrentOutput / reactor.MaxOutput);
                }

                IMyInventory inv = slim.FatBlock.GetInventory(0);
                if (inv == null) continue;

                blockCount++;
                total += inv.MaxVolume;
                current += inv.CurrentVolume;
            }

            if (total > 0 && consumptionRate > 0)
            {
                double percent = (((double)current * 1000d) / ((double)total * 1000d) * 100d);
                double tick = (((double)current * 1000d) / consumptionRate);

                //StringBuilder data = new StringBuilder($"Fuel: {percent.ToString("n0")}% {TimeSpan.FromMilliseconds((tick / 60d) * 1000d).ToString("g").Split('.')[0]}");

                MyAPIGateway.Utilities.ShowNotification($"Fuel: {percent.ToString("n0")}% {TimeSpan.FromMilliseconds((tick / 60d) * 1000d).ToString("g").Split('.')[0]}", 1);
            }
        }
    }

    public class Consumption : MyGameLogicComponent
    {
        public static MyObjectBuilder_PhysicalObject PhysicalFuelObject = null;
        public static readonly List<string> FuelStorage = new List<string>() { "SB1x3x3cargo", "SB1x5x5cargo", "hmindustrialtank_Large" };
        public double MaxConsumptionPerTick = 0;

        private IMyReactor ModBlock;
        private IMyInventory Inventory;
        private float fuelSearch = 0;

        public void initialInit(double rate)
        {
            MaxConsumptionPerTick = rate;
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            ModBlock = Entity as IMyReactor;

            MaxConsumptionPerTick = (MaxConsumptionPerTick / 60f) - (1 / 60 / 60 / 60 * ModBlock.MaxOutput);

            if (PhysicalFuelObject == null)
            {
                PhysicalFuelObject = new MyObjectBuilder_PhysicalObject();
                PhysicalFuelObject = new MyObjectBuilder_Ingot() { SubtypeName = "Diesel" };
            }

            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            Inventory = ModBlock.GetInventory(0);
        }

        public override void UpdateBeforeSimulation()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            if (ModBlock.CurrentOutput == 0) return;
            if (!ModBlock.IsFunctional || !ModBlock.IsWorking) return;
            if (PhysicalFuelObject == null) return;

            MyDefinitionId id = new MyDefinitionId(PhysicalFuelObject.TypeId, PhysicalFuelObject.SubtypeId);
            float powerRatio = ModBlock.CurrentOutput / ModBlock.MaxOutput;
            double consumptionAmount = MaxConsumptionPerTick * powerRatio;

            MyFixedPoint quanity = Inventory.GetItemAmount(id);

            if (quanity < 200 && fuelSearch == 0)
            {
                List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                ModBlock.CubeGrid.GetBlocks(blocks, b => b.FatBlock != null && !b.FatBlock.BlockDefinition.IsNull() && FuelStorage.Contains(b.FatBlock.BlockDefinition.SubtypeId));

                foreach (IMySlimBlock block in blocks)
                {
                    IMyInventory inv = (block.FatBlock as IMyTerminalBlock).GetInventory(0);

                    if (inv == null) continue;

                    if (!Inventory.IsConnectedTo(inv)) continue;

                    MyFixedPoint value = inv.GetItemAmount(id);

                    if (value == null) continue;

                    if (value > 200)
                    {
                        inv.RemoveItemsOfType(200, PhysicalFuelObject, false);
                        Inventory.AddItems(200, PhysicalFuelObject);
                        break;
                    }
                    else if (value < 200)
                    {
                        inv.RemoveItemsOfType(value, PhysicalFuelObject, false);
                        Inventory.AddItems(value, PhysicalFuelObject);

                        if (Inventory.ContainItems(200, PhysicalFuelObject))
                        {
                            break;
                        }
                    }
                }
            }

            if (quanity > (MyFixedPoint)consumptionAmount)
            {
                Inventory.RemoveItemsOfType((MyFixedPoint)consumptionAmount, PhysicalFuelObject, false);
            }

            if (fuelSearch > 0)
            {
                fuelSearch--;
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "V8Engine")]
    public class V8Engine : Consumption
    {
        public V8Engine()
        {
            initialInit(4d);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "V8EngineTurbo")]
    public class V8EngineTurbo : Consumption
    {
        public V8EngineTurbo()
        {
            initialInit(7d);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "hmdieselgenerator3_Large")]
    public class hmdieselgenerator3_Large : Consumption
    {
        public hmdieselgenerator3_Large()
        {
            initialInit(3d);
        }
    }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), false, "hmdieselgenerator1_Large")]
    public class hmdieselgenerator1_Large : Consumption
    {
        public hmdieselgenerator1_Large()
        {
            initialInit(0.5d);
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
            MyInventory inv = ((MyInventory)Entity.GetInventory(0));

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
            else
            {
            }
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