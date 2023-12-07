﻿using CustomAlbums.Data;
using CustomAlbums.Managers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomAlbums.Utilities
{
    public static class SaveExtensions
    {
        private static readonly Logger Logger = new(nameof(SaveExtensions));

        /// <summary>
        /// Gets the chart save data given the chart UID.
        /// </summary>
        /// <param name="save">The save file data class.</param>
        /// <param name="uid">The chart UID.</param>
        /// <returns>A JsonObject consisting of score information from the current chart's UID.</returns>
        public static JsonObject GetChartSaveDataFromUid(this CustomAlbumsSave save, string uid)
        {
            var album = AlbumManager.GetByUid(uid);
            var key = $"album_{Path.GetFileNameWithoutExtension(album.Path)}";
            return new JsonObject()
            {
                { nameof(save.Highest), JsonNode.Parse(JsonSerializer.Serialize(save.Highest.GetValueOrDefault(key))) },
                { nameof(save.FullCombo), JsonNode.Parse(JsonSerializer.Serialize(save.FullCombo.GetValueOrDefault(key))) }
            };
        }
    }
}
