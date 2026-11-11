using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Sets up a character model's import and builds its animator: a base layer for the whole body,
/// and an upper-body layer so carrying and smashing can play while the legs keep running. Each of
/// Astra's body types has its own skeleton and clips, so each gets its own controller.
/// </summary>
public static class CargoClashCharacterAnimator
{
    private const string Folder = "Assets/Game/Animation";

    private static readonly HashSet<string> LoopingClips = new HashSet<string>
    {
        "Idle", "Run", "Carry", "CarryBig", "CoCarryIdle", "CoCarryWalk", "Smash", "ReadyHammer", "ReadyVacuum", "Blast", "Charge", "Cheer", "Grumble",
    };

    public static void ConfigureImport(string modelPath, float scale = 1f, bool blendShapes = false)
    {
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.globalScale = scale;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.importAnimation = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = blendShapes;
        importer.resampleCurves = true;
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        importer.SaveAndReimport();

        // Blender names each take "Armature|Run". The game uses the short name.
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            string shortName = clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1);
            clip.name = shortName;
            clip.loopTime = LoopingClips.Contains(shortName);
            clip.loopPose = false;
            clip.lockRootRotation = false;
            clip.lockRootHeightY = false;
            clip.lockRootPositionXZ = false;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    public static AnimatorController BuildController(string modelPath, Transform modelRoot, string name = "Character")
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game", "Animation");

        Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clips[clip.name] = clip;
        }
        foreach (string needed in new[] { "Idle", "Run", "Carry", "CarryBig", "CoCarryIdle", "CoCarryWalk", "Ram", "Knocked", "KnockedOut", "Smash", "Toss", "ReadyHammer", "ReadyVacuum", "Blast", "Charge", "Slam", "Flinch", "Cheer", "Grumble" })
        {
            if (!clips.ContainsKey(needed)) Debug.LogError($"[Builder] {name} has no {needed} clip.");
        }

        string path = $"{Folder}/{name}.controller";
        AssetDatabase.DeleteAsset(path);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Rate", AnimatorControllerParameterType.Float);
        controller.AddParameter("Carry", AnimatorControllerParameterType.Int);
        controller.AddParameter("Dash", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Knocked", AnimatorControllerParameterType.Bool);
        controller.AddParameter("KnockedRate", AnimatorControllerParameterType.Float);
        controller.AddParameter("Out", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Mood", AnimatorControllerParameterType.Int);
        controller.AddParameter("UpperPose", AnimatorControllerParameterType.Int);

        BuildBaseLayer(controller, clips);
        BuildUpperLayer(controller, clips, modelRoot, name);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void BuildBaseLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips)
    {
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState locomotion = BlendState(controller, "Locomotion", clips["Idle"], clips["Run"]);
        machine.defaultState = locomotion;
        AnimatorState coCarry = BlendState(controller, "CoCarry", clips["CoCarryIdle"], clips["CoCarryWalk"]);
        AnimatorState ram = ClipState(machine, "Ram", clips["Ram"]);
        AnimatorState knocked = ClipState(machine, "Knocked", clips["Knocked"]);
        knocked.speedParameterActive = true;
        knocked.speedParameter = "KnockedRate";
        AnimatorState knockedOut = ClipState(machine, "KnockedOut", clips["KnockedOut"]);
        AnimatorState cheer = ClipState(machine, "Cheer", clips["Cheer"]);
        AnimatorState grumble = ClipState(machine, "Grumble", clips["Grumble"]);

        Transition(locomotion, coCarry, 0.12f).AddCondition(AnimatorConditionMode.Equals, 2, "Carry");
        Transition(coCarry, locomotion, 0.12f).AddCondition(AnimatorConditionMode.NotEqual, 2, "Carry");

        AnimatorStateTransition toKnocked = AnyTransition(machine, knocked, 0.04f);
        toKnocked.AddCondition(AnimatorConditionMode.If, 0, "Knocked");
        toKnocked.AddCondition(AnimatorConditionMode.IfNot, 0, "Out");
        Transition(knocked, locomotion, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
        AnyTransition(machine, knockedOut, 0.04f).AddCondition(AnimatorConditionMode.If, 0, "Out");
        Transition(knockedOut, locomotion, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0, "Out");

        AnimatorStateTransition toRam = AnyTransition(machine, ram, 0.04f);
        toRam.AddCondition(AnimatorConditionMode.If, 0, "Dash");
        toRam.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
        Transition(ram, locomotion, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Dash");

        foreach ((AnimatorState mood, int value) in new[] { (cheer, 1), (grumble, 2) })
        {
            AnimatorStateTransition enter = AnyTransition(machine, mood, 0.15f);
            enter.AddCondition(AnimatorConditionMode.Equals, value, "Mood");
            enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Knocked");
            Transition(mood, locomotion, 0.15f).AddCondition(AnimatorConditionMode.Equals, 0, "Mood");
        }
    }

    private static void BuildUpperLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips, Transform modelRoot, string name)
    {
        controller.AddLayer("UpperBody");
        AnimatorControllerLayer[] layers = controller.layers;
        layers[1].defaultWeight = 0f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1].avatarMask = BuildUpperBodyMask(modelRoot, name);
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[1].stateMachine;
        AnimatorState carry = ClipState(machine, "Carry", clips["Carry"]);
        AnimatorState smash = ClipState(machine, "Smash", clips["Smash"]);
        AnimatorState toss = ClipState(machine, "Toss", clips["Toss"]);
        AnimatorState carryBig = ClipState(machine, "CarryBig", clips["CarryBig"]);
        AnimatorState readyHammer = ClipState(machine, "ReadyHammer", clips["ReadyHammer"]);
        AnimatorState readyVacuum = ClipState(machine, "ReadyVacuum", clips["ReadyVacuum"]);
        AnimatorState blast = ClipState(machine, "Blast", clips["Blast"]);
        AnimatorState charge = ClipState(machine, "Charge", clips["Charge"]);
        AnimatorState slam = ClipState(machine, "Slam", clips["Slam"]);
        AnimatorState flinch = ClipState(machine, "Flinch", clips["Flinch"]);
        machine.defaultState = carry;

        AnyTransition(machine, carry, 0.08f).AddCondition(AnimatorConditionMode.Equals, 1, "UpperPose");
        AnyTransition(machine, smash, 0.08f).AddCondition(AnimatorConditionMode.Equals, 2, "UpperPose");
        AnyTransition(machine, toss, 0.04f).AddCondition(AnimatorConditionMode.Equals, 3, "UpperPose");
        AnyTransition(machine, carryBig, 0.08f).AddCondition(AnimatorConditionMode.Equals, 4, "UpperPose");
        AnyTransition(machine, flinch, 0.03f).AddCondition(AnimatorConditionMode.Equals, 6, "UpperPose");
        AnyTransition(machine, readyHammer, 0.14f).AddCondition(AnimatorConditionMode.Equals, 7, "UpperPose");
        AnyTransition(machine, readyVacuum, 0.14f).AddCondition(AnimatorConditionMode.Equals, 8, "UpperPose");
        AnyTransition(machine, blast, 0.04f).AddCondition(AnimatorConditionMode.Equals, 9, "UpperPose");
        AnyTransition(machine, charge, 0.12f).AddCondition(AnimatorConditionMode.Equals, 10, "UpperPose");
        AnyTransition(machine, slam, 0.03f).AddCondition(AnimatorConditionMode.Equals, 11, "UpperPose");
    }

    /// <summary>The spine, neck, head, arms and fingers. The hips and legs stay with the base layer.</summary>
    private static AvatarMask BuildUpperBodyMask(Transform modelRoot, string name)
    {
        string path = $"{Folder}/{name}_UpperBody.mask";
        AssetDatabase.DeleteAsset(path);
        AvatarMask mask = new AvatarMask { name = "UpperBody" };

        List<string> paths = new List<string>();
        List<bool> active = new List<bool>();
        foreach (Transform item in modelRoot.GetComponentsInChildren<Transform>(true))
        {
            if (item == modelRoot) continue;
            string bone = item.name;
            bool upper = bone == "Spine" || bone == "Chest" || bone == "Neck" || bone == "Head" || bone == "Stars" ||
                bone.EndsWith("Shoulder") || bone.EndsWith("UpperArm") || bone.EndsWith("LowerArm") || bone.EndsWith("Hand") ||
                bone.Contains("Thumb") || bone.Contains("Index") || bone.Contains("Middle") || bone.Contains("Ring") || bone.Contains("Little");
            paths.Add(AnimationUtility.CalculateTransformPath(item, modelRoot));
            active.Add(upper);
        }
        mask.transformCount = paths.Count;
        for (int i = 0; i < paths.Count; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, active[i]);
        }
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }

    private static AnimatorState BlendState(AnimatorController controller, string name, AnimationClip still, AnimationClip moving)
    {
        AnimatorState state = controller.CreateBlendTreeInController(name, out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(still, 0f);
        tree.AddChild(moving, 0.3f);
        state.speedParameterActive = true;
        state.speedParameter = "Rate";
        return state;
    }

    private static AnimatorState ClipState(AnimatorStateMachine machine, string name, AnimationClip clip)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        return state;
    }

    private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float seconds)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        Configure(transition, seconds);
        return transition;
    }

    private static AnimatorStateTransition AnyTransition(AnimatorStateMachine machine, AnimatorState to, float seconds)
    {
        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        Configure(transition, seconds);
        transition.canTransitionToSelf = false;
        return transition;
    }

    private static void Configure(AnimatorStateTransition transition, float seconds)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = seconds;
        transition.exitTime = 0f;
    }
}
