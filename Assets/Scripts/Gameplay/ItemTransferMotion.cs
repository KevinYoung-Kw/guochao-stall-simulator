using System;
using UnityEngine;

namespace Guochao
{
    // Keep the authored final pose authoritative; only interpolate the visible transfer.
    [DefaultExecutionOrder(150)]
    public sealed class ItemTransferMotion : MonoBehaviour
    {
        private Vector3 origin, destination;
        private Quaternion originRotation, destinationRotation;
        private float elapsed, duration;
        private Action finished;
        public static bool EnabledForRun
        {
            get
            {
                if (!Application.isPlaying || Application.isBatchMode) return false;
                string[] args = Environment.GetCommandLineArgs();
                return Array.IndexOf(args,"--smoke-test")<0 && Array.IndexOf(args,"--traversal-test")<0 && Array.IndexOf(args,"--capture-dir")<0;
            }
        }
        public static void Begin(GameObject item, Vector3 from, Quaternion rotation, Action completed = null)
        {
            if (!EnabledForRun) { completed?.Invoke(); return; }
            var motion = item.GetComponent<ItemTransferMotion>() ?? item.AddComponent<ItemTransferMotion>();
            motion.origin=from; motion.originRotation=rotation;
            motion.destination=item.transform.localPosition; motion.destinationRotation=item.transform.localRotation;
            motion.elapsed=0; motion.duration=.16f; motion.finished=completed; motion.enabled=true;
            item.transform.SetPositionAndRotation(from,rotation);
        }
        private void LateUpdate()
        {
            if (GameFlow.Instance == null || !GameFlow.Instance.Run.Paused) elapsed+=Time.deltaTime;
            float t=Mathf.Clamp01(elapsed/duration), smooth=t*t*(3-2*t);
            Transform parent=transform.parent;
            Vector3 goal=parent ? parent.TransformPoint(destination) : destination;
            Quaternion rotation=parent ? parent.rotation*destinationRotation : destinationRotation;
            transform.SetPositionAndRotation(Vector3.Lerp(origin,goal,smooth)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.09f), Quaternion.Slerp(originRotation,rotation,smooth));
            if(t<1)return;
            enabled=false; var callback=finished;finished=null;callback?.Invoke();
        }
    }
}
