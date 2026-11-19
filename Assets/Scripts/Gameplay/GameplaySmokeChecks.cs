using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Guochao
{
    public static class GameplaySmokeChecks
    {
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        public static string RunModelChecks()
        {
            RunState r = new RunState();
            Require(!r.Act("StovePickup", false) && !r.HoldingStove, "Remote stove pickup must fail");
            Require(r.Act("StovePickup", true) && r.HoldingStove && !r.StoveLoaded, "Home pickup");
            Require(!r.Act("HomeCart", false) && !r.StoveLoaded, "Remote stove loading must fail");
            Require(r.Act("HomeCart", true) && r.StoveLoaded && !r.HoldingStove, "Home loading");
            Require(r.Act("HomeExit", true) && r.Phase == GamePhase.Market && r.StoveLoaded, "Home to Market retains B04");
            Require(r.Act("RiceShelf", true) && r.Act("MeatShelf", true) && r.Act("EggShelf", true), "Independent shelf pickups");
            Require(r.Cart.Count == 3 && r.CartTotalCents == 6120 && r.CashCents == 32000, "Unpaid three-box sample and cash conservation");
            Require(!r.ReturnItem(2, "RiceShelf", true) && r.Cart.Count == 3, "Return requires matching shelf");
            Require(r.ReturnItem(2, "EggShelf", true) && r.CartTotalCents == 4500, "One-box return");
            Require(r.Act("EggShelf", true), "Returned sample is available again");
            Require(!r.Act("MarketCart", true) && r.Cart.Count == 3, "Unpaid stock cannot load");
            Require(!r.Act("Cashier", false) && r.CashCents == 32000, "Remote checkout must fail");
            Require(r.CanPrimary("EggShelf",true) && r.Act("EggShelf",true) && r.Cart.Count==4, "Primary shelf action takes another real carton");
            Require(r.ReturnItem(3,"EggShelf",true) && r.Cart.Count==3, "Explicit return removes only one repeated carton");
            Require(r.Act("Cashier", true) && r.CheckoutInProgress && r.CashCents == 32000 && !r.Paid, "Manual checkout begins waiting without charging");
            Require(!r.CanPrimary("Cashier",true) && !r.CanPrimary("EggShelf",true), "Service disables all primary business actions");
            r.Tick(r.CheckoutDuration*.5f,true);
            Require(!r.Paid && r.CashCents==32000, "Partial service does not charge");
            r.Tick(.1f,false);
            Require(!r.CheckoutInProgress && !r.Paid && r.CashCents==32000, "Leaving cashier cancels service without charging");
            Require(r.Act("Cashier",true),"Can queue again after interruption");
            r.Tick(r.CheckoutDuration,true);
            Require(r.CashCents == 25880 && r.Paid && r.Cargo.Count == 0, "Completed manual service charges once, stock stays in trolley");
            Require(!r.Act("Cashier", true) && r.CashCents == 25880, "Repeated checkout must not charge twice");
            Require(!r.Act("MarketCart", false) && r.Cargo.Count == 0, "Remote loading must fail");
            Require(!r.Act("MarketExit", true), "Cannot leave with paid unloaded stock");
            Require(r.Act("MarketCart", true) && r.Cargo.Count == 3 && r.Cart.Count == 0 && r.StoveLoaded, "Loading retains B04 and moves all purchased stock");
            r.Tick(78-r.MarketUsed);
            Require(r.StartTime == "08:18" && Mathf.Abs(r.Remaining - 42) < .01f, "78 seconds means 08:18");
            Require(r.Act("MarketExit", true) && r.Phase == GamePhase.Complete, "Exit reaches preparation summary");

            RunState timeout = new RunState();
            timeout.Act("StovePickup", true); timeout.Tick(60);
            Require(timeout.Phase == GamePhase.Market && !timeout.HoldingStove && !timeout.StoveLoaded, "Home timeout must not silently load held stove");
            timeout.Act("RiceShelf", true); timeout.Act("MeatShelf", true); timeout.Act("EggShelf", true);
            timeout.Paused = true; timeout.Tick(25);
            Require(timeout.Remaining == 120, "Pause freezes timer");
            timeout.Paused = false; timeout.Tick(120);
            Require(timeout.Phase == GamePhase.Complete && timeout.TimeoutSettlement && timeout.CashCents == 25880 && timeout.Cargo.Count == 3 && timeout.StartTime == "09:00", "120-second auto-settlement");
            RunState loadedTimeout = new RunState();
            loadedTimeout.Act("StovePickup", true); loadedTimeout.Act("HomeCart", true); loadedTimeout.Tick(60);
            Require(loadedTimeout.StoveLoaded && loadedTimeout.Phase == GamePhase.Market, "Home timeout preserves loaded B04");
            RunState serviceTimeout = new RunState();
            serviceTimeout.Act("HomeExit",true);serviceTimeout.Act("RiceShelf",true);serviceTimeout.Tick(118);
            serviceTimeout.Act("Cashier",true);serviceTimeout.Tick(2,true);
            Require(serviceTimeout.Phase==GamePhase.Complete && serviceTimeout.CashCents==30650 && serviceTimeout.SpentCents==1350 && !serviceTimeout.CheckoutInProgress,"Timeout during manual service charges exactly once");
            RunState paidTimeout = new RunState();
            paidTimeout.Act("HomeExit",true);paidTimeout.Act("RiceShelf",true);paidTimeout.Act("Cashier",true);
            paidTimeout.Tick(paidTimeout.CheckoutDuration,true);paidTimeout.Tick(120-paidTimeout.MarketUsed);
            Require(paidTimeout.Phase==GamePhase.Complete && paidTimeout.CashCents==30650 && paidTimeout.SpentCents==1350 && paidTimeout.Cargo.Count==1,"Timeout after payment must load without a second charge");
            return "MODEL_PASS: authoritative primary action, repeat pickup and explicit return, manual service delay/interruption, payment once, cash conservation, remote rejection, load retention, timeout during service, pause, 78s→08:18.";
        }

        public static string RunAll(GameFlow flow)
        {
            StringBuilder report = new StringBuilder("GAMEPLAY_SMOKE\n");
            report.AppendLine(RunModelChecks());
            flow.Initialize();
            Require(flow.State.phase != GamePhase.Error, flow.State.message);
            report.AppendLine(CheckWorld(flow.World, new[] { "StovePickup", "HomeCart", "HomeExit" }));
            MoveToAndAct(flow, "StovePickup"); Require(flow.Run.HoldingStove, "Scene pickup");
            MoveToAndAct(flow, "HomeCart"); Require(flow.Run.StoveLoaded, "Scene loading");
            MoveToAndAct(flow, "HomeExit"); Require(flow.Run.Phase == GamePhase.Market, "Scene transition");
            report.AppendLine(CheckWorld(flow.World, new[] { "RiceShelf", "MeatShelf", "EggShelf", "Cashier", "MarketCart", "MarketExit" }));
            report.AppendLine(CheckTrolley(flow.World.ShoppingMotor));
            MoveToAndAct(flow, "RiceShelf"); MoveToAndAct(flow, "MeatShelf"); MoveToAndAct(flow, "EggShelf");
            Require(flow.Run.Cart.Count == 3, "Scene independent pickups");
            MoveToAndAct(flow, "Cashier"); Require(flow.Run.CheckoutInProgress && !flow.State.primaryEnabled, "Scene checkout begins a real disabled service stage");
            flow.Run.Tick(flow.Run.CheckoutDuration,true); Require(flow.Run.CashCents == 25880, "Scene checkout completes after service time");
            MoveToAndAct(flow, "MarketCart"); Require(flow.Run.Cargo.Count == 3 && flow.Run.StoveLoaded, "Scene loading and B04 retention");
            report.AppendLine(CheckLoadedVehicle(flow.World));
            MoveToAndAct(flow, "MarketExit"); Require(flow.Run.Phase == GamePhase.Complete, "Scene completion");
            report.AppendLine("SCENE_PASS: local actions across real HomeWorld/MarketWorld and preparation summary.");
            report.AppendLine("GAMEPLAY_SMOKE_PASS");
            return report.ToString();
        }

        private static void MoveToAndAct(GameFlow flow, string target)
        {
            flow.World.Player.Teleport(flow.World.Anchor(target) + Vector3.up * .04f);
            Physics.SyncTransforms(); flow.PrimaryAction();
        }

        private static string CheckLoadedVehicle(WorldRuntime world)
        {
            Transform vehicle = world.transform.Find("CartAI_Runtime");
            Require(vehicle != null, "The actual Aholo vehicle must replace the old cart");
            Transform origin = null, forward = null, slot0 = null, slot2 = null;
            foreach (Transform t in vehicle.GetComponentsInChildren<Transform>())
            {
                if (t.name == "CartOrigin") origin = t;
                if (t.name == "CartForward") forward = t;
                if (t.name == "CargoSlot0") slot0 = t;
                if (t.name == "CargoSlot2") slot2 = t;
            }
            Require(origin != null && forward != null && slot0 != null && slot2 != null,
                "Vehicle frame and cargo anchors are required for physical inspection");
            // Imported FBX empties can carry negative axes and a 100x marker scale.
            // Construct a metre-space frame from the actual anchor positions.
            Vector3 rearDirection = (origin.position - forward.position).normalized;
            Vector3 acrossDirection = (slot2.position - slot0.position).normalized;
            Bounds[] boxes = new Bounds[3];
            string[] names = { "RiceBox_Runtime", "MeatBox_Runtime", "EggBox_Runtime" };
            for (int i = 0; i < names.Length; i++)
            {
                Transform item = world.transform.Find(names[i]);
                Require(item != null, "Loaded item missing: " + names[i]);
                Require((item.localScale - Vector3.one).sqrMagnitude < .0001f, "Cargo must retain its authored size");
                bool first = true;
                foreach (MeshFilter mesh in item.GetComponentsInChildren<MeshFilter>())
                    foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    {
                        Vector3 relative = mesh.transform.TransformPoint(vertex) - origin.position;
                        Vector3 point = new Vector3(Vector3.Dot(relative, rearDirection),
                            Vector3.Dot(relative, acrossDirection), Vector3.Dot(relative, Vector3.up));
                        if (first) { boxes[i] = new Bounds(point, Vector3.zero); first = false; }
                        else boxes[i].Encapsulate(point);
                    }
                Require(!first, "Loaded item mesh is empty");
                Bounds b = boxes[i];
                Require(b.min.x >= 1.644f && b.max.x <= 2.076f && b.min.y >= -.576f && b.max.y <= .576f,
                    "Cargo exceeds the real rear-rack footprint: " + names[i] + " " + b);
                Require(Mathf.Abs(b.min.z - .54f) < .003f && b.max.z < .974f,
                    "Cargo must rest on the rack and remain below the worktop: " + names[i]);
            }
            for (int i = 0; i < boxes.Length; i++) for (int j = i + 1; j < boxes.Length; j++)
                Require(!boxes[i].Intersects(boxes[j]), "Loaded boxes intersect");
            return "VEHICLE_PASS: actual Aholo body, independent B04, three unscaled boxes inside the rear rack with no overlap.";
        }

        private static string CheckTrolley(LinkedTrolleyMotor motor)
        {
            Require(motor!=null,"Market needs the linked physical trolley motor");
            Require(motor.Width>.84f && motor.Width<.92f,$"Expected the actual 0.88m cart width, got {motor.Width}");
            Transform shopper=motor.GetComponent<WorldRuntime>().Player.transform;
            Vector3 savedPosition=shopper.position;
            Quaternion savedFacing=motor.GetComponent<WorldRuntime>().Player.visual.rotation;
            GameObject fixtures=new GameObject("TrolleyCollisionValidationOnly");
            Vector3 origin=new Vector3(1000,0,1000);
            try
            {
                BoxCollider floor=TestBox(fixtures.transform,origin+new Vector3(0,-.1f,2),new Vector3(14,.2f,18));
                BoxCollider left=TestBox(fixtures.transform,origin+new Vector3(-.5f,1,4),new Vector3(.2f,2,8));
                BoxCollider right=TestBox(fixtures.transform,origin+new Vector3(.5f,1,4),new Vector3(.2f,2,8));
                motor.ResetPoseForValidation(origin+new Vector3(0,.03f,-3),Quaternion.identity);
                for(int i=0;i<40;i++)motor.TryMove(Vector3.forward,2,.1f);
                Require(motor.Blocked && shopper.position.z<origin.z+.1f,"0.80m aisle must block a 0.88m cart although the shopper fits");
                float blockedZ=shopper.position.z;
                motor.TryMove(Vector3.back,2,.25f);
                Require(shopper.position.z<blockedZ-.4f,"A blocked trolley must be able to back out without rotating through the shelf");
                left.transform.position=origin+new Vector3(-.60f,1,4);
                right.transform.position=origin+new Vector3(.60f,1,4);
                motor.ResetPoseForValidation(origin+new Vector3(0,.03f,-3),Quaternion.identity);
                for(int i=0;i<30;i++)motor.TryMove(Vector3.forward,2,.1f);
                Require(shopper.position.z>origin.z+2.5f,"1.00m straight aisle must pass the real cart");
                left.gameObject.SetActive(false);right.gameObject.SetActive(false);
                BoxCollider turningPole=TestBox(fixtures.transform,origin+new Vector3(1.1f,.65f,1.1f),new Vector3(.12f,1.3f,.12f));
                motor.ResetPoseForValidation(origin+Vector3.up*.03f,Quaternion.identity);
                Require(motor.IsPoseClear(shopper.position,Quaternion.identity)&&motor.IsPoseClear(shopper.position,Quaternion.Euler(0,90,0)),"Turning fixture must leave both endpoint poses clear");
                motor.TryMove(Vector3.right,0,1);
                Require(motor.Blocked && Quaternion.Angle(motor.GetComponent<WorldRuntime>().Player.visual.rotation,Quaternion.Euler(0,90,0))>5,"Turning sweep must stop before the pole, even when final pose is clear");
                return $"TROLLEY_PASS: actual width {motor.Width:0.00}m; narrow aisle blocks both shopper/cart; 1.00m aisle passes; turning-arc obstacle blocks before contact; no rear teleport.";
            }
            finally
            {
                fixtures.SetActive(false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(fixtures);else UnityEngine.Object.DestroyImmediate(fixtures);
                motor.ResetPoseForValidation(savedPosition,savedFacing);
            }
        }

        private static BoxCollider TestBox(Transform parent,Vector3 position,Vector3 size)
        {
            GameObject obj=new GameObject("ValidationCollision");obj.transform.SetParent(parent);obj.transform.position=position;
            obj.layer=WorldRuntime.EnvironmentLayer;BoxCollider collider=obj.AddComponent<BoxCollider>();collider.size=size;
            Physics.SyncTransforms();return collider;
        }

        private static string CheckWorld(WorldRuntime world, string[] targets)
        {
            Require(world != null && world.Player != null, "Real world and Aholo player must exist");
            Require(world.SolidColliders.Count > 2, "World needs floor, wall and furniture collision");
            foreach (string target in targets)
            {
                world.Player.Teleport(world.Anchor(target) + Vector3.up * .04f); Physics.SyncTransforms();
                Require(world.IsNear(target), "Interaction anchor inaccessible or inside obstruction: " + target);
            }
            CheckCollision(world);
            int reachable = CheckReachability(world, targets);
            world.Player.Teleport(world.Anchor("Spawn") + Vector3.up * .04f);
            return $"WORLD_PASS {world.gameObject.name}: {world.SolidColliders.Count} colliders; {reachable}/{targets.Length} interaction points reachable; CharacterController blocks furniture.";
        }

        private static void CheckCollision(WorldRuntime world)
        {
            float ground = world.Anchor("Spawn").y;
            foreach (Collider collider in world.SolidColliders)
            {
                Bounds b = collider.bounds;
                if (b.size.y < .65f || b.min.y > ground + 1.2f || b.size.x > 8 || b.size.z < .6f) continue;
                Vector3 start = new Vector3(b.min.x - .65f, ground + .04f, b.center.z);
                world.Player.Teleport(start); Physics.SyncTransforms();
                world.Player.Controller.Move(Vector3.right * (b.size.x + 1.3f));
                Require(world.Player.transform.position.x < b.max.x + .35f, "Character passed through collision proxy: " + collider.name);
                return;
            }
            throw new InvalidOperationException("No suitable furniture collider found for movement test");
        }

        private static int CheckReachability(WorldRuntime world, string[] targets)
        {
            const float step = .4f;
            Vector3 spawn = world.Anchor("Spawn");
            float minX = spawn.x, maxX = spawn.x, minZ = spawn.z, maxZ = spawn.z;
            foreach (string target in targets)
            {
                Vector3 p = world.Anchor(target);
                minX = Mathf.Min(minX,p.x); maxX = Mathf.Max(maxX,p.x); minZ = Mathf.Min(minZ,p.z); maxZ = Mathf.Max(maxZ,p.z);
            }
            minX -= 3; minZ -= 3; maxX += 3; maxZ += 3;
            int width = Mathf.CeilToInt((maxX-minX)/step)+1, depth = Mathf.CeilToInt((maxZ-minZ)/step)+1;
            bool[,] open = new bool[width,depth], seen = new bool[width,depth];
            for (int x=0;x<width;x++) for(int z=0;z<depth;z++)
            {
                Vector3 p = new Vector3(minX+x*step,spawn.y,minZ+z*step);
                bool ground = Physics.Raycast(p+Vector3.up*.22f,Vector3.down,.6f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                open[x,z] = ground && !Physics.CheckCapsule(p+Vector3.up*.33f,p+Vector3.up*1.37f,.25f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
            }
            int sx=Mathf.Clamp(Mathf.RoundToInt((spawn.x-minX)/step),0,width-1),sz=Mathf.Clamp(Mathf.RoundToInt((spawn.z-minZ)/step),0,depth-1);
            Require(open[sx,sz], "Spawn is blocked in collision navigation check");
            Queue<Vector2Int> queue = new Queue<Vector2Int>(); queue.Enqueue(new Vector2Int(sx,sz)); seen[sx,sz]=true;
            Vector2Int[] directions={Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
            while(queue.Count>0)
            {
                Vector2Int current=queue.Dequeue();
                foreach(Vector2Int d in directions)
                {
                    Vector2Int n=current+d;
                    if(n.x<0||n.y<0||n.x>=width||n.y>=depth||seen[n.x,n.y]||!open[n.x,n.y])continue;
                    seen[n.x,n.y]=true;queue.Enqueue(n);
                }
            }
            int count=0;
            foreach(string target in targets)
            {
                Vector3 p=world.Anchor(target);bool reachable=false;
                for(int x=0;x<width&&!reachable;x++)for(int z=0;z<depth&&!reachable;z++)
                {
                    if(!seen[x,z])continue;
                    Vector3 cell=new Vector3(minX+x*step,spawn.y,minZ+z*step);
                    Vector3 delta=cell-p;delta.y=0;
                    if(delta.sqrMagnitude<1.5f*1.5f&&!Physics.Linecast(cell+Vector3.up*.8f,p+Vector3.up*.8f,WorldRuntime.EnvironmentMask))reachable=true;
                }
                Require(reachable,"No walkable route from Spawn to "+target);count++;
            }
            return count;
        }
    }
}
