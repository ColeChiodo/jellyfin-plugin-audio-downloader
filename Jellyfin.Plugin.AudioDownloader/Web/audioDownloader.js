(function () {
    'use strict';

    var PLUGIN_ID = 'a1d6f0e2-5c3b-4e8a-9f7d-2e4b8c1a9f34';
    var VIDEO_TYPES = ['Movie', 'Series', 'Season', 'Episode'];

    var currentItemId = null;
    var buttonAdded = false;
    var timer = null;

    function getApiClient() {
        if (window.ApiClient) {
            return window.ApiClient;
        }

        return null;
    }

    function getQueryParam(name) {
        var match = window.location.hash.match(new RegExp('[?&]' + name + '=([^&]+)'));
        return match ? decodeURIComponent(match[1]) : null;
    }

    function showLoading() {
        try {
            window.Dashboard.showLoadingMsg();
        } catch (e) {
            /* noop */
        }
    }

    function hideLoading() {
        try {
            window.Dashboard.hideLoadingMsg();
        } catch (e) {
            /* noop */
        }
    }

    function toast(message) {
        try {
            window.Dashboard.alert({ text: message });
        } catch (e) {
            alert(message);
        }
    }

    function trackTitle(track) {
        if (track.DisplayTitle) {
            return track.DisplayTitle;
        }

        var title = ((track.Language || '') + ' ' + (track.Codec || '')).trim();
        return title || ('Track ' + track.Index);
    }

    function sanitizeName(name) {
        return (name || 'audio').replace(/[\\/:*?"<>|]/g, '_');
    }

    function buildDownloadUrl(itemId, streamIndex, format, token) {
        var apiClient = getApiClient();
        if (apiClient && typeof apiClient.getUrl === 'function') {
            return apiClient.getUrl('AudioDownloader/download', {
                itemId: itemId,
                stream: streamIndex,
                format: format,
                api_key: token
            });
        }

        return 'AudioDownloader/download?itemId=' + encodeURIComponent(itemId) +
            '&stream=' + encodeURIComponent(streamIndex) +
            '&format=' + encodeURIComponent(format) +
            '&api_key=' + encodeURIComponent(token);
    }

    function startDownload(item, streamIndex, format) {
        var apiClient = getApiClient();
        if (!apiClient) {
            toast('API client not available.');
            return;
        }

        var token = typeof apiClient.accessToken === 'function' ? apiClient.accessToken() : '';
        var url = buildDownloadUrl(item.Id, streamIndex, format, token);

        var anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = sanitizeName(item.Name) + '.' + (format === 'mp3' ? 'mp3' : 'm4a');
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
    }

    function closeDialog() {
        var dialog = document.getElementById('audioDownloaderDialog');
        if (dialog && dialog.parentNode) {
            dialog.parentNode.removeChild(dialog);
        }
    }

    function buildDialog(item, tracks, defaultFormat) {
        var dialog = document.createElement('div');
        dialog.id = 'audioDownloaderDialog';
        dialog.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.55);z-index:99999;display:flex;align-items:center;justify-content:center;';
        var card = document.createElement('div');
        card.style.cssText = 'background:#1b1b1b;color:#ddd;padding:24px;border-radius:8px;min-width:340px;max-width:480px;box-shadow:0 6px 24px rgba(0,0,0,0.5);';

        var title = document.createElement('h3');
        title.textContent = 'Download Audio';
        title.style.cssText = 'margin:0 0 4px;color:#fff;';
        card.appendChild(title);

        var itemName = document.createElement('div');
        itemName.textContent = item.Name || '';
        itemName.style.cssText = 'opacity:0.72;margin-bottom:16px;font-size:0.9em;';
        card.appendChild(itemName);

        var formatLabel = document.createElement('label');
        formatLabel.textContent = 'Format: ';
        formatLabel.style.cssText = 'margin-right:8px;';
        var formatSelect = document.createElement('select');
        var mp3Option = document.createElement('option');
        mp3Option.value = 'mp3';
        mp3Option.textContent = 'MP3';
        var m4aOption = document.createElement('option');
        m4aOption.value = 'm4a';
        m4aOption.textContent = 'M4A (AAC)';
        formatSelect.appendChild(mp3Option);
        formatSelect.appendChild(m4aOption);
        formatSelect.value = defaultFormat;
        card.appendChild(formatLabel);
        card.appendChild(formatSelect);
        card.appendChild(document.createElement('br'));

        var trackSelect = null;
        if (tracks.length > 1) {
            var trackLabel = document.createElement('label');
            trackLabel.textContent = 'Audio track: ';
            trackLabel.style.cssText = 'margin-right:8px;';
            trackSelect = document.createElement('select');
            tracks.forEach(function (track) {
                var option = document.createElement('option');
                option.value = String(track.Index);
                option.textContent = trackTitle(track);
                if (track.IsDefault) {
                    option.selected = true;
                }

                trackSelect.appendChild(option);
            });
            card.appendChild(document.createElement('br'));
            card.appendChild(trackLabel);
            card.appendChild(trackSelect);
        }

        var buttonRow = document.createElement('div');
        buttonRow.style.cssText = 'margin-top:20px;text-align:right;';

        var cancelButton = document.createElement('button');
        cancelButton.textContent = 'Cancel';
        cancelButton.style.cssText = 'margin-right:12px;padding:8px 16px;';
        cancelButton.addEventListener('click', closeDialog);

        var downloadButton = document.createElement('button');
        downloadButton.textContent = 'Download';
        downloadButton.style.cssText = 'padding:8px 16px;';
        downloadButton.addEventListener('click', function () {
            var streamIndex = trackSelect ? parseInt(trackSelect.value, 10) : -1;
            closeDialog();
            startDownload(item, streamIndex, formatSelect.value);
        });

        buttonRow.appendChild(cancelButton);
        buttonRow.appendChild(downloadButton);
        card.appendChild(buttonRow);
        dialog.appendChild(card);
        document.body.appendChild(dialog);
    }

    function showChooser(item, tracks) {
        var apiClient = getApiClient();
        var defaultFormat = 'm4a';

        var defaultsPromise = apiClient && typeof apiClient.getPluginConfiguration === 'function'
            ? apiClient.getPluginConfiguration(PLUGIN_ID)
            : Promise.resolve(null);

        showLoading();
        defaultsPromise.then(function (config) {
            hideLoading();
            if (config && config.DefaultFormat === 'Mpeg3') {
                defaultFormat = 'mp3';
            }

            buildDialog(item, tracks, defaultFormat);
        }).catch(function () {
            hideLoading();
            buildDialog(item, tracks, 'm4a');
        });
    }

    function onDownloadClicked(itemId) {
        var apiClient = getApiClient();
        if (!apiClient || typeof apiClient.getItem !== 'function') {
            toast('API client not available.');
            return;
        }

        apiClient.getItem(apiClient.getCurrentUserId(), itemId).then(function (item) {
            if (!apiClient || typeof apiClient.getJSON !== 'function') {
                toast('API client not available.');
                return;
            }

            showLoading();
            apiClient.getJSON(apiClient.getUrl('AudioDownloader/tracks', { itemId: itemId })).then(function (tracks) {
                hideLoading();
                tracks = tracks || [];
                if (tracks.length === 0) {
                    toast('No audio tracks found for this item.');
                    return;
                }

                showChooser(item, tracks);
            }).catch(function () {
                hideLoading();
                toast('Failed to load audio tracks.');
            });
        }).catch(function () {
            toast('Failed to load item details.');
        });
    }

    function addButton(itemId) {
        var container = document.querySelector('.detailPagePrimaryContent');
        if (!container) {
            return;
        }

        if (container.querySelector('#audioDownloaderButton')) {
            return;
        }

        var button = document.createElement('button');
        button.id = 'audioDownloaderButton';
        button.type = 'button';
        button.textContent = 'Download Audio';
        button.className = 'button-link emby-button';
        button.style.cssText = 'margin-left:12px;';
        button.addEventListener('click', function () {
            onDownloadClicked(itemId);
        });

        container.appendChild(button);
        buttonAdded = true;
        currentItemId = itemId;
    }

    function removeButton() {
        var existing = document.getElementById('audioDownloaderButton');
        if (existing && existing.parentNode) {
            existing.parentNode.removeChild(existing);
        }

        buttonAdded = false;
        currentItemId = null;
    }

    function validateAndAdd(itemId) {
        if (buttonAdded && currentItemId === itemId) {
            return;
        }

        var apiClient = getApiClient();
        if (!apiClient || typeof apiClient.getItem !== 'function') {
            return;
        }

        apiClient.getItem(apiClient.getCurrentUserId(), itemId).then(function (item) {
            if (VIDEO_TYPES.indexOf(item.Type) >= 0) {
                addButton(itemId);
            } else if (buttonAdded) {
                removeButton();
            }
        }).catch(function () { });
    }

    function handleRoute() {
        if (window.location.hash.indexOf('#/details?') !== 0) {
            if (timer) {
                window.clearInterval(timer);
                timer = null;
            }

            removeButton();
            return;
        }

        var itemId = getQueryParam('id');
        if (!itemId) {
            return;
        }

        if (timer) {
            window.clearInterval(timer);
        }

        timer = window.setInterval(function () {
            if (document.querySelector('.detailPagePrimaryContent')) {
                window.clearInterval(timer);
                timer = null;
                validateAndAdd(itemId);
            }
        }, 400);
    }

    window.addEventListener('hashchange', handleRoute);
    document.addEventListener('DOMContentLoaded', handleRoute);
    handleRoute();
})();