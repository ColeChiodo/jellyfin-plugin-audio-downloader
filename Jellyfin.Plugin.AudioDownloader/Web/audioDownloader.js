(function () {
    'use strict';

    var PLUGIN_ID = 'a1d6f0e2-5c3b-4e8a-9f7d-2e4b8c1a9f34';
    var VIDEO_TYPES = ['Movie', 'Series', 'Season', 'Episode'];

    var currentItemId = null;
    var pollCount = 0;
    var button = null;

    function log(level, message) {
        try {
            var line = '[AudioDownloader] ' + message;
            if (level === 'error') {
                console.error(line);
            } else {
                console.warn(line);
            }
        } catch (e) { /* noop */ }
    }

    function getApiClient() {
        if (window.ApiClient) {
            return window.ApiClient;
        }

        if (window.connectionManager && window.connectionManager.currentApiClient) {
            var client = window.connectionManager.currentApiClient;
            if (typeof client === 'function') {
                return client();
            }

            return client;
        }

        return null;
    }

    function getToken(apiClient) {
        if (!apiClient) {
            return '';
        }

        try {
            if (typeof apiClient.accessToken === 'function') {
                return apiClient.accessToken() || '';
            }

            if (apiClient.accessToken) {
                return apiClient.accessToken;
            }
        } catch (e) { /* noop */ }

        return '';
    }

    function getBaseUrl(apiClient) {
        if (!apiClient) {
            return '';
        }

        try {
            if (typeof apiClient.serverAddress === 'function') {
                return apiClient.serverAddress();
            }

            if (typeof apiClient.getServerAddress === 'function') {
                return apiClient.getServerAddress();
            }

            if (apiClient.serverAddress) {
                return apiClient.serverAddress;
            }
        } catch (e) { /* noop */ }

        return '';
    }

    function buildUrl(path, params) {
        var apiClient = getApiClient();
        if (apiClient && typeof apiClient.getUrl === 'function') {
            try {
                return apiClient.getUrl(path, params);
            } catch (e) { /* noop */ }
        }

        var url = getBaseUrl(apiClient) + '/' + path;
        var query = [];
        for (var key in (params || {})) {
            if (Object.prototype.hasOwnProperty.call(params, key)) {
                query.push(encodeURIComponent(key) + '=' + encodeURIComponent(params[key]));
            }
        }

        if (query.length) {
            url += '?' + query.join('&');
        }

        return url;
    }

    function request(path, params) {
        var token = getToken(getApiClient());
        var url = buildUrl(path, params);
        var headers = {};
        if (token) {
            headers['X-Emby-Token'] = token;
        }

        return fetch(url, {
            headers: headers
        }).then(function (response) {
            if (!response.ok) {
                throw new Error(path + ' returned ' + response.status);
            }

            return response.json();
        });
    }

    function currentUserId(apiClient) {
        if (!apiClient) {
            return null;
        }

        try {
            if (typeof apiClient.getCurrentUserId === 'function') {
                var id = apiClient.getCurrentUserId();
                if (id) {
                    return id;
                }
            }
        } catch (e) { /* noop */ }

        return null;
    }

    function getItem(itemId) {
        var apiClient = getApiClient();
        var userId = currentUserId(apiClient);

        if (apiClient && typeof apiClient.getItem === 'function' && userId) {
            return apiClient.getItem(userId, itemId);
        }

        return request('Users/Me').then(function (me) {
            return request('Users/' + me.Id + '/Items/' + itemId);
        });
    }

    function getQueryParam(name) {
        var match = window.location.hash.match(new RegExp('[?&]' + name + '=([^&]+)'));
        return match ? decodeURIComponent(match[1]) : null;
    }

    function showLoading() {
        try {
            window.Dashboard.showLoadingMsg();
        } catch (e) { /* noop */ }
    }

    function hideLoading() {
        try {
            window.Dashboard.hideLoadingMsg();
        } catch (e) { /* noop */ }
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

    function startDownload(item, streamIndex, format) {
        var token = getToken(getApiClient());
        var url = buildUrl('AudioDownloader/download', {
            itemId: item.Id,
            stream: streamIndex,
            format: format,
            api_key: token
        });

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
        var defaultFormat = 'm4a';
        showLoading();

        request('Plugins/' + PLUGIN_ID + '/Configuration').then(function (config) {
            hideLoading();
            if (config && config.DefaultFormat === 'Mpeg3') {
                defaultFormat = 'mp3';
            }

            buildDialog(item, tracks, defaultFormat);
        }).catch(function (err) {
            hideLoading();
            log('info', 'config fetch failed (' + (err && err.message) + '), using defaults');
            buildDialog(item, tracks, 'm4a');
        });
    }

    function loadTracks(itemId) {
        return request('AudioDownloader/tracks', { itemId: itemId });
    }

    function onDownloadClicked(itemId) {
        getItem(itemId).then(function (item) {
            showLoading();
            loadTracks(itemId).then(function (tracks) {
                hideLoading();
                tracks = tracks || [];
                if (tracks.length === 0) {
                    toast('No audio tracks found for this item.');
                    return;
                }

                showChooser(item, tracks);
            }).catch(function (err) {
                hideLoading();
                log('error', 'failed to load tracks: ' + (err && err.message));
                toast('Failed to load audio tracks.');
            });
        }).catch(function (err) {
            log('error', 'failed to load item details: ' + (err && err.message));
            toast('Failed to load item details.');
        });
    }

    function findButtonContainer() {
        var candidates = [
            '.mainDetailButtons',
            '.detailPagePrimaryContent',
            '.detailPagePrimaryContainer',
            '.detailRibbon',
            '#itemDetailPage',
            '.headerRight'
        ];
        for (var i = 0; i < candidates.length; i++) {
            var container = document.querySelector(candidates[i]);
            if (container) {
                return { el: container, selector: candidates[i] };
            }
        }

        return null;
    }

    function dumpDetailClasses() {
        try {
            var found = {};
            [
                '.mainDetailButtons',
                '.detailPagePrimaryContent',
                '.detailPagePrimaryContainer',
                '.detailPageContent',
                '.detailRibbon',
                '#itemDetailPage',
                '.detailButton',
                '.headerRight',
                '.detailSection'
            ].forEach(function (sel) {
                found[sel] = !!document.querySelector(sel);
            });

            log('info', 'DOM probe: ' + JSON.stringify(found));
        } catch (e) { /* noop */ }
    }

    function dumpTopClasses() {
        try {
            var counts = {};
            document.querySelectorAll('[class]').forEach(function (el) {
                var name = typeof el.className === 'string' ? el.className : '';
                if (!name) {
                    return;
                }

                name.split(/\s+/).forEach(function (cls) {
                    if (!cls || cls === 'ltr') {
                        return;
                    }

                    counts[cls] = (counts[cls] || 0) + 1;
                });
            });

            var top = Object.keys(counts).map(function (cls) {
                return cls + '(' + counts[cls] + ')';
            }).sort(function (a, b) {
                var an = parseInt(a.replace(/^.*\((\d+)\)$/, '$1'), 10);
                var bn = parseInt(b.replace(/^.*\((\d+)\)$/, '$1'), 10);
                return bn - an;
            }).slice(0, 30);

            log('info', 'top classes: ' + top.join(', '));
        } catch (e) { /* noop */ }
    }

    function validateAndAdd(itemId, container) {
        if (currentItemId === itemId && document.getElementById('audioDownloaderButton')) {
            return;
        }

        if (currentItemId !== itemId) {
            removeButton();
            currentItemId = itemId;
        }

        getItem(itemId).then(function (item) {
            var type = item && item.Type;
            log('info', 'resolved item type: ' + type);
            if (VIDEO_TYPES.indexOf(type) >= 0) {
                addButton(itemId, container);
            } else {
                log('info', 'item type ' + type + ' is not video, skipping button');
            }
        }).catch(function (err) {
            log('warn', 'could not resolve item type (' + (err && err.message) + '), adding button optimistically');
            addButton(itemId, container);
        });
    }

    function addButton(itemId, container) {
        if (document.getElementById('audioDownloaderButton')) {
            return;
        }

        button = document.createElement('button');
        button.id = 'audioDownloaderButton';
        button.type = 'button';
        button.textContent = 'Download Audio';
        button.className = 'button-link emby-button';

        if (container.selector === '.headerRight') {
            button.style.cssText = 'margin-left:8px;';
        } else {
            button.style.cssText = 'margin-left:12px;';
        }

        button.addEventListener('click', function () {
            onDownloadClicked(itemId);
        });

        container.el.appendChild(button);
        currentItemId = itemId;
        log('info', 'button added to "' + container.selector + '" for item ' + itemId);
    }

    function removeButton() {
        var existing = document.getElementById('audioDownloaderButton');
        if (existing && existing.parentNode) {
            existing.parentNode.removeChild(existing);
        }

        button = null;
        currentItemId = null;
    }

    var lastHash = null;
    var lastNotFoundLog = 0;
    var topDumpedFor = null;

    function tick() {
        var hash = window.location.hash || '';
        if (hash !== lastHash) {
            lastHash = hash;
            log('info', 'route: "' + hash + '"');
            removeButton();
            currentItemId = null;
        }

        var itemId = getQueryParam('id');
        if (!itemId) {
            return;
        }

        var container = findButtonContainer();
        if (!container) {
            pollCount++;
            var now = Date.now();
            if (!lastNotFoundLog || now - lastNotFoundLog > 4000) {
                lastNotFoundLog = now;
                log('info', 'detail container not found (itemId ' + itemId + ', ' + (pollCount * 0.5).toFixed(1) + 's)');
            }

            if (topDumpedFor !== hash) {
                topDumpedFor = hash;
                dumpDetailClasses();
                dumpTopClasses();
            }

            return;
        }

        if (currentItemId !== itemId) {
            removeButton();
        }

        if (document.getElementById('audioDownloaderButton')) {
            pollCount = 0;
            return;
        }

        validateAndAdd(itemId, container);
        pollCount = 0;
    }

    log('info', 'script loaded');
    window.addEventListener('hashchange', tick);
    document.addEventListener('DOMContentLoaded', tick);
    window.setInterval(tick, 500);
    tick();
})();