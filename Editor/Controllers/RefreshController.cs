using System;
using MrTerrainPainter.Editor.State;

namespace MrTerrainPainter.Editor.Controllers
{
    public class RefreshController : IRefreshController
    {
        private readonly EditorState state;
        private readonly Action refreshVegetationList;
        private readonly Action refreshPreviewList;
        private readonly Action updatePropertyPanel;

        public RefreshController(
            EditorState state,
            Action refreshVegetationList,
            Action refreshPreviewList,
            Action updatePropertyPanel)
        {
            this.state = state ?? new EditorState();
            this.refreshVegetationList = refreshVegetationList ?? throw new ArgumentNullException(nameof(refreshVegetationList));
            this.refreshPreviewList = refreshPreviewList ?? throw new ArgumentNullException(nameof(refreshPreviewList));
            this.updatePropertyPanel = updatePropertyPanel ?? throw new ArgumentNullException(nameof(updatePropertyPanel));
        }

        public void RefreshAllUI()
        {
            refreshVegetationList?.Invoke();
            refreshPreviewList?.Invoke();
            updatePropertyPanel?.Invoke();
        }
    }
}