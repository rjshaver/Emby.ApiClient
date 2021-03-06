﻿using Emby.ApiInteraction.Data;
using MediaBrowser.Model.ApiClient;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.ApiInteraction.Sync
{
    public class OfflineUserSync : IOfflineUserSync
    {
        private readonly ILogger _logger;
        private readonly ILocalAssetManager _localAssetManager;

        public OfflineUserSync(ILocalAssetManager localAssetManager, ILogger logger)
        {
            _localAssetManager = localAssetManager;
            _logger = logger;
        }

        public async Task UpdateOfflineUsers(ServerInfo server, IApiClient apiClient, CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var user in server.Users.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await SaveOfflineUser(user, apiClient, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Already logged at lower level
                }
            }
        }

        private async Task SaveOfflineUser(ServerUserInfo user, IApiClient apiClient, CancellationToken cancellationToken)
        {
            var deleteUser = false;
            var updateImage = false;

            UserDto offlineUser = null;

            try
            {
                offlineUser = await apiClient.GetOfflineUserAsync(user.Id).ConfigureAwait(false);

                await _localAssetManager.SaveOfflineUser(offlineUser).ConfigureAwait(false);

                updateImage = true;
            }
            catch (HttpException ex)
            {
                _logger.ErrorException("Error getting user info", ex);

                if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.NotFound)
                {
                    deleteUser = true;
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error getting user info", ex);
            }

            if (deleteUser)
            {
                await _localAssetManager.DeleteOfflineUser(user.Id).ConfigureAwait(false);
            }

            if (updateImage && offlineUser != null)
            {
                await UpdateUserImage(offlineUser, apiClient, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task UpdateUserImage(UserDto user, IApiClient apiClient, CancellationToken cancellationToken)
        {
            if (user.HasPrimaryImage)
            {
                var isImageCached = await _localAssetManager.HasImage(user).ConfigureAwait(false);

                if (!isImageCached)
                {
                    var imageUrl = apiClient.GetUserImageUrl(user, new ImageOptions
                    {
                        ImageType = ImageType.Primary
                    });

                    using (var stream = await apiClient.GetImageStreamAsync(imageUrl, cancellationToken).ConfigureAwait(false))
                    {
                        await _localAssetManager.SaveImage(user, stream).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                await _localAssetManager.DeleteImage(user).ConfigureAwait(false);
            }
        }
    }
}
