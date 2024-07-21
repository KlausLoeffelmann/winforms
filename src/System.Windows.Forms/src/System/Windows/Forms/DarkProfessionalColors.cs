// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using static System.Windows.Forms.Control;

namespace System.Windows.Forms
{
    internal class DarkProfessionalColors : ProfessionalColorTable
    {
        public override Color MenuItemPressedGradientBegin
            => Color.FromArgb(0xFF, 0x60, 0x60, 0x60);

        public override Color MenuItemPressedGradientMiddle
            => Color.FromArgb(0xFF, 0x60, 0x60, 0x60);

        public override Color MenuItemPressedGradientEnd
            => Color.FromArgb(0xFF, 0x60, 0x60, 0x60);

        public override Color MenuItemSelected
            => ControlSystemColors.DefaultDarkMode.ControlText;

        public override Color MenuItemSelectedGradientBegin
            => Color.FromArgb(0xFF, 0x40, 0x40, 0x40);

        public override Color MenuItemSelectedGradientEnd
            => Color.FromArgb(0xFF, 0x40, 0x40, 0x40);

        public override Color MenuStripGradientBegin
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color MenuStripGradientEnd
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color StatusStripGradientBegin
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color StatusStripGradientEnd
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color ToolStripDropDownBackground
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color ImageMarginGradientBegin
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color ImageMarginGradientMiddle
            => ControlSystemColors.DefaultDarkMode.Control;

        public override Color ImageMarginGradientEnd
            => ControlSystemColors.DefaultDarkMode.Control;
    }
}
