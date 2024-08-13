// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Extensions;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Wiki;

namespace osu.Game.Overlays
{
    public partial class WikiOverlay : OnlineOverlay<WikiHeader>
    {
        public const string INDEX_PATH = @"Main_page";

        public string CurrentPath => path.Value;

        private readonly Bindable<string> path = new Bindable<string>(INDEX_PATH);

        private readonly Bindable<APIWikiPage> wikiData = new Bindable<APIWikiPage>();

        [Resolved]
        private IAPIProvider api { get; set; }

        private GetWikiRequest request;

        private CancellationTokenSource cancellationToken;

        private bool displayUpdateRequired = true;

        private WikiArticlePage articlePage;

        private Bindable<Language> language;

        public WikiOverlay()
            : base(OverlayColourScheme.Orange, false)
        {
        }

        [BackgroundDependencyLoader]
        private void load(OsuGameBase game)
        {
            // Fetch current language on load for translated pages (if possible)
            language = game.CurrentLanguage.GetBoundCopy();
        }

        public void ShowPage(string pagePath = INDEX_PATH)
        {
            path.Value = pagePath.Trim('/');
            Show();
        }

        protected override WikiHeader CreateHeader() => new WikiHeader
        {
            ShowIndexPage = () => ShowPage(),
            ShowParentPage = showParentPage,
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();
            path.BindValueChanged(onPathChanged);
            wikiData.BindTo(Header.WikiPageData);
        }

        protected override void PopIn()
        {
            base.PopIn();

            if (displayUpdateRequired)
            {
                path.TriggerChange();
                displayUpdateRequired = false;
            }
        }

        protected override void PopOutComplete()
        {
            base.PopOutComplete();
            displayUpdateRequired = true;
        }

        protected void LoadDisplay(Drawable display)
        {
            ScrollFlow.ScrollToStart();
            LoadComponentAsync(display, loaded =>
            {
                Child = loaded;
                Loading.Hide();
            }, (cancellationToken = new CancellationTokenSource()).Token);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (articlePage != null)
            {
                articlePage.SidebarContainer.Height = DrawHeight;
                articlePage.SidebarContainer.Y = Math.Clamp(ScrollFlow.Current - Header.DrawHeight, 0, Math.Max(ScrollFlow.ScrollContent.DrawHeight - DrawHeight - Header.DrawHeight, 0));
            }
        }

        private void onPathChanged(ValueChangedEvent<string> e)
        {
            // the path could change as a result of redirecting to a newer location of the same page.
            // we already have the correct wiki data, so we can safely return here.
            if (e.NewValue == wikiData.Value?.Path)
                return;

            if (e.NewValue == "error")
                return;

            cancellationToken?.Cancel();
            request?.Cancel();

            // Language code + path, or just path1 + path2 in case
            string[] values = e.NewValue.Split('/', 2);

            if (values.Length > 1 && LanguageExtensions.TryParseCultureCode(values[0], out var lang))
                request = new GetWikiRequest(values[1], lang);
            else
                request = new GetWikiRequest(e.NewValue, language.Value);

            Loading.Show();

            request.Success += response => Schedule(() => onSuccess(response));
            request.Failure += ex =>
            {
                if (ex is not OperationCanceledException)
                    Schedule(onFail, request.Path);
            };

            api.PerformAsync(request);
        }

        private void onSuccess(APIWikiPage response)
        {
            wikiData.Value = response;
            path.Value = response.Path;

            if (response.Layout.Equals(INDEX_PATH, StringComparison.OrdinalIgnoreCase))
            {
                LoadDisplay(new WikiMainPage
                {
                    Markdown = response.Markdown,
                    Padding = new MarginPadding
                    {
                        Vertical = 20,
                        Horizontal = HORIZONTAL_PADDING,
                    },
                });
            }
            else
            {
                LoadDisplay(articlePage = new WikiArticlePage($@"{api.WebsiteRootUrl}/wiki/{path.Value}/", response.Markdown));
            }
        }

        private void onFail(string originalPath)
        {
            wikiData.Value = null;
            path.Value = "error";

            LoadDisplay(articlePage = new WikiArticlePage($@"{api.WebsiteRootUrl}/wiki/",
                $"Something went wrong when trying to fetch page \"{originalPath}\".\n\n[Return to the main page]({INDEX_PATH})."));
        }

        private void showParentPage()
        {
            string parentPath = string.Join("/", path.Value.Split('/').SkipLast(1));
            ShowPage(parentPath);
        }

        protected override void Dispose(bool isDisposing)
        {
            cancellationToken?.Cancel();
            request?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
