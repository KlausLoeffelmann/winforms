﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Rendering.Animation;

namespace System.Windows.Forms.Rendering.CheckBox;

internal class AnimatedToggleSwitchRenderer : AnimatedControlRenderer
{
    private const int AnimationDuration = 300; // milliseconds

    private readonly ModernCheckBoxStyle _switchStyle;

    public AnimatedToggleSwitchRenderer(Control control, ModernCheckBoxStyle switchStyle)
        : base(control)
    {
        _switchStyle = switchStyle;
    }

    private Forms.CheckBox CheckBox => (Forms.CheckBox)Control;

    public override void AnimationProc(float animationProgress)
    {
        base.AnimationProc(animationProgress);
        Invalidate();
    }

    protected override (int animationDuration, AnimationCycle animationCycle) OnAnimationStarted()
    {
        AnimationProgress = 1;
        return (AnimationDuration, AnimationCycle.Once);
    }

    /// <summary>
    ///  Called from OnPaint of the control. If we only want the render animations, we need to make sure,
    ///  that this was triggered by AnimationProc and we know the relative progress.
    /// </summary>
    /// <param name="graphics">The graphics objects to render in.</param>
    public override void RenderControl(Graphics graphics)
    {
        int dpiScale = DpiScale;

        int switchWidth = 50 * dpiScale;
        int switchHeight = 25 * dpiScale;
        int circleDiameter = 20 * dpiScale;

        Size textSize = TextRenderer.MeasureText(Control.Text, Control.Font);

        int totalHeight = Math.Max(textSize.Height, switchHeight);
        int switchY = (totalHeight - switchHeight) / 2;
        int textY = (totalHeight - textSize.Height) / 2;

        graphics.Clear(Control.BackColor);

        switch (CheckBox.TextAlign)
        {
            case ContentAlignment.MiddleLeft:
            case ContentAlignment.TopLeft:
            case ContentAlignment.BottomLeft:
                RenderSwitch(graphics, new Rectangle(textSize.Width + 10 * DpiScale, switchY, switchWidth, switchHeight), circleDiameter);
                RenderText(graphics, new Point(0, textY));
                break;

            default:
                RenderSwitch(graphics, new Rectangle(0, switchY, switchWidth, switchHeight), circleDiameter);
                RenderText(graphics, new Point(switchWidth + 10 * DpiScale, textY));
                break;
        }
    }

    private void RenderText(Graphics g, Point position) =>
        TextRenderer.DrawText(g, CheckBox.Text, CheckBox.Font, position, CheckBox.ForeColor);

    private void RenderSwitch(Graphics g, Rectangle rect, int circleDiameter)
    {
        Color backgroundColor = CheckBox.Checked ^ (AnimationProgress < 0.8f)
            ? SystemColors.Highlight
            : SystemColors.ControlDark;

        Color circleColor = SystemColors.ControlText;

        // This works both for when the Animation is running, and when it's not running.
        // In the latter case we set the animationProgress to 1, so the circle is drawn
        // at the correct position.
        float circlePosition = CheckBox.Checked
            ? (rect.Width - circleDiameter) * (1 - EaseOut(AnimationProgress))
            : (rect.Width - circleDiameter) * EaseOut(AnimationProgress);

        using var backgroundBrush = backgroundColor.GetCachedSolidBrushScope();
        using var circleBrush = circleColor.GetCachedSolidBrushScope();
        using var backgroundPen = SystemColors.WindowFrame.GetCachedPenScope(2 * DpiScale);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_switchStyle == ModernCheckBoxStyle.Rounded)
        {
            float radius = rect.Height / 2f;

            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();

            g.FillPath(backgroundBrush, path);
            g.DrawPath(backgroundPen, path);
        }
        else
        {
            g.FillRectangle(backgroundBrush, rect);
            g.DrawRectangle(backgroundPen, rect);
        }

        g.FillEllipse(circleBrush, rect.X + circlePosition, rect.Y + 2.5f * DpiScale, circleDiameter, circleDiameter);

        static float EaseOut(float t) => (1 - t) * (1 - t);
    }

    protected override void OnAnimationStopped()
    {
        AnimationProgress = 0;
    }

    protected override void OnAnimationEnded()
    {
        AnimationProgress = 1;
    }
}
