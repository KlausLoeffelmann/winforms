// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Rendering.Animation;

internal partial class AnimationManager
{
    private class AnimationRendererItem(AnimatedControlRenderer renderer, int animationDuration, AnimationCycle animationCycle)
    {
        public long StopwatchTarget;

        public AnimatedControlRenderer Renderer { get; } = renderer;
        public int TriggerFrequency { get; }
        public int AnimationDuration { get; set; } = animationDuration;
        public int FrameCount { get; set; }
        public AnimationCycle AnimationCycle { get; set; } = animationCycle;
        public int FrameOffset { get; set; } = 1;
    }
}
