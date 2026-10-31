using System;
using BetterFG.Services;

namespace BetterFG.UI
{
    public class UGCTab : SwitchTab
    {
        public UGCTab(IntPtr ptr) : base(ptr) { }

        private SkinRepo _selectedRepo;

        public SkinRepo SelectedRepo
        {
            get => _selectedRepo ?? (_selectedRepo = RepoRegistry.Instance?.Active);
            set => _selectedRepo = value;
        }

        public bool ImportedSelected;
        public bool FeaturedSelected;

        public virtual void OnRepoChanged() { }

        protected string SelectedRaw => SelectedRepo?.RawBase;

        protected bool FetchSelectedRepo()
        {
            var catalog = CustomizationServices.CatalogService;
            if (catalog == null || SelectedRepo == null) return false;
            catalog.FetchSkins(SelectedRepo);
            return catalog.IsFetching;
        }
    }
}
