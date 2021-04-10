using System;
using SixLabors.ImageSharp;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace SysBot.ACNHOrders
{
    public class DodoDraw
    {
        private string ImagePathTemplate { get; set; } = "dodo.png";
        private string FontPath { get; set; } = "dodo.ttf";
        private string ImagePathOutput => "current" + ImagePathTemplate;

        private readonly FontCollection FontCollection = new();
        private readonly FontFamily DodoFontFamily;
        private readonly Font DodoFont;
        private readonly Image BaseImage;

        private readonly TextOptions options;
        private readonly TextGraphicsOptions tOptions;

        public DodoDraw(float fontPercentage = 100)
        {
            DodoFontFamily = FontCollection.Install(FontPath);
            BaseImage = Image.Load(ImagePathTemplate);
            DodoFont = DodoFontFamily.CreateFont(BaseImage.Height * 0.4f * (fontPercentage/100f), FontStyle.Regular);

            options = new TextOptions()
            {
                ApplyKerning = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            tOptions = new TextGraphicsOptions()
            {
                TextOptions = options
            };
        }

        public string Draw(string dodo)
        {
            using (var img = BaseImage.Clone(x => x.DrawText(tOptions, dodo, DodoFont, Color.White, new PointF(BaseImage.Width * 0.5f, BaseImage.Height * 0.38f))))
            {
                img.Save(ImagePathOutput);
            }

            return ImagePathOutput;
        }

        public string? GetProcessedDodoImagePath()
        {
            if (File.Exists(ImagePathOutput))
                return ImagePathOutput;

            return null;
        }
    }
}
