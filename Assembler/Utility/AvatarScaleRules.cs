using System.Collections.Generic;
using System.Reflection;

using RobloxFiles.DataTypes;

namespace Rbx2Source.Assembler
{
    public class AvatarScaleRules
    {
        private static readonly Dictionary<string, FieldInfo> fields;

        static AvatarScaleRules()
        {
            fields = new Dictionary<string, FieldInfo>();

            foreach (FieldInfo field in typeof(AvatarScaleRules)
                .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                fields[field.Name] = field;
            }
        }

        public Vector3 Head;
        public Vector3 UpperTorso;
        public Vector3 LowerTorso;

        public Vector3 LeftUpperArm;
        public Vector3 LeftLowerArm;
        public Vector3 LeftHand;

        public Vector3 LeftUpperLeg;
        public Vector3 LeftLowerLeg;
        public Vector3 LeftFoot;

        public Vector3 RightUpperArm;
        public Vector3 RightLowerArm;
        public Vector3 RightHand;

        public Vector3 RightUpperLeg;
        public Vector3 RightLowerLeg;
        public Vector3 RightFoot;

        public Vector3 this[string limbName]
        {
            get
            {
                if (fields.TryGetValue(limbName, out FieldInfo field))
                    return field.GetValue(this) as Vector3;

                return new Vector3(1, 1, 1);
            }
        }
    }
}
