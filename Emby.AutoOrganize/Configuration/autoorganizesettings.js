define(['mainTabsManager', 'globalize','emby-input', 'emby-select', 'emby-checkbox', 'emby-button', 'emby-collapse', 'emby-toggle'], function (mainTabsManager, globalize) {
    'use strict';

    ApiClient.getFileOrganizationResults = function (options) {

        var url = this.getUrl("Library/FileOrganization", options || {});

        return this.getJSON(url);
    };

    ApiClient.deleteOriginalFileFromOrganizationResult = function (id) {

        var url = this.getUrl("Library/FileOrganizations/" + id + "/File");

        return this.ajax({
            type: "DELETE",
            url: url
        });
    };

    ApiClient.clearOrganizationLog = function () {

        var url = this.getUrl("Library/FileOrganizations");

        return this.ajax({
            type: "DELETE",
            url: url
        });
    };

    ApiClient.performOrganization = function (id) {

        var url = this.getUrl("Library/FileOrganizations/" + id + "/Organize");

        return this.ajax({
            type: "POST",
            url: url
        });
    };

    ApiClient.performEpisodeOrganization = function (id, options) {

        var url = this.getUrl("Library/FileOrganizations/" + id + "/Episode/Organize");

        return this.ajax({
            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: 'application/json'
        });
    };

    ApiClient.performMovieOrganization = function (id, options) {

        var url = this.getUrl("Library/FileOrganizations/" + id + "/Movie/Organize");

        return this.ajax({
            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: 'application/json'
        });
    };

    ApiClient.getSmartMatchInfos = function (options) {

        options = options || {};

        var url = this.getUrl("Library/FileOrganizations/SmartMatches", options);

        return this.ajax({
            type: "GET",
            url: url,
            dataType: "json"
        });
    };

    ApiClient.deleteSmartMatchEntries = function (entries) {

        var url = this.getUrl("Library/FileOrganizations/SmartMatches/Delete");

        var postData = {
            Entries: entries
        };

        return this.ajax({

            type: "POST",
            url: url,
            data: JSON.stringify(postData),
            contentType: "application/json"
        });
    };

    function getMovieFileName(value) {
        var movieName = "Movie Name";
        var movieYear = "2017";
        var fileNameWithoutExt = movieName + '.' + movieYear + '.MULTI.1080p.BluRay.Directors.Cut.DTS.x264-UTT';

        var result = value.replace('%mn', movieName)
            .replace('%m.n', movieName.replace(' ', '.'))
            .replace('%m_n', movieName.replace(' ', '_'))
            .replace('%my', movieYear)
            .replace('%ext', 'mkv')             
            .replace('%res', '1080p')
            .replace('%e', "Directors Cut")
            .replace('%fn', fileNameWithoutExt);

        return result;
    }

    function getMovieFolderFileName(value) {
        var movieName = "Movie Name";
        var movieYear = "2017";
        var fileNameWithoutExt = movieName + '.' + movieYear + '.MULTI.1080p.BluRay.Directors.Cut.DTS.x264-UTT';

        var result = value.replace('%mn', movieName)
            .replace('%m.n', movieName.replace(' ', '.'))
            .replace('%m_n', movieName.replace(' ', '_'))
            .replace('%my', movieYear)
            .replace('%ext', 'mkv')
            .replace('%fn', fileNameWithoutExt);

        return result;
    }

    

    function getEpisodeFileName(value, enableMultiEpisode) {

        var seriesName = "Series Name";
        var episodeTitle = "Episode Four";
        var fileName = seriesName + ' ' + episodeTitle;

        var result = value.replace('%sn', seriesName)
            .replace('%s.n', seriesName.replace(' ', '.'))
            .replace('%s_n', seriesName.replace(' ', '_'))
            .replace('%s', '1')
            .replace('%0s', '01')
            .replace('%00s', '001')
            .replace('%ext', 'mkv')
            .replace('%en', episodeTitle)
            .replace('%e.n', episodeTitle.replace(' ', '.'))
            .replace('%e_n', episodeTitle.replace(' ', '_'))
            .replace('%fn', fileName);

        if (enableMultiEpisode) {
            result = result
                .replace('%ed', '5')
                .replace('%0ed', '05')
                .replace('%00ed', '005');
        }

        return result
            .replace('%e', '4')
            .replace('%0e', '04')
            .replace('%00e', '004');
    }

    function getSeriesDirecoryName(value) {

        var seriesName = "Series Name";
        var seriesYear = "2017";
        var fullName = seriesName + ' (' + seriesYear + ')';

        return value.replace('%sn', seriesName)
            .replace('%s.n', seriesName.replace(' ', '.'))
            .replace('%s_n', seriesName.replace(' ', '_'))
            .replace('%sy', seriesYear)
            .replace('%fn', fullName);
    }

    function loadPage(view, config) {

        //view.querySelector('#chkEnableScheduledTask').checked = config.EnableScheduledTask;

        view.querySelector('#chkEnableTvSorting').checked = config.IsEpisodeSortingEnabled;

        view.querySelector('#chkOverwriteExistingItems').checked = config.OverwriteExistingFiles;

        view.querySelector('#chkDeleteEmptyFolders').checked = config.DeleteEmptyFolders;

        view.querySelector('#txtMinFileSize').value = config.MinFileSizeMb;

        view.querySelector('#txtSeasonFolderPattern').value = config.SeasonFolderPattern;

        view.querySelector('#txtSeasonZeroName').value = config.SeasonZeroFolderName;

        view.querySelector('#txtWatchFolder').value = config.WatchLocations[0] || '';

        //view.querySelector('.watchFolderListContainer').innerHTML = getWatchedLocationListItemHtml(config.WatchLocations)
        var watchLocationList = view.querySelector('.watchFolderListContainer');
        watchLocationList.innerHTML = getWatchedLocationListItemHtml(config.WatchLocations);
        var removeButtons = watchLocationList.querySelectorAll('Button');
        if (removeButtons) {
          removeButtons.forEach(btn => btn.addEventListener('click', async (e) => await removeWatchedFolder(e, view)))
        }


        view.querySelector('#txtEpisodePattern').value = config.EpisodeNamePattern;

        view.querySelector('#txtMultiEpisodePattern').value = config.MultiEpisodeNamePattern;

        view.querySelector('#txtIgnoreFileNameContains').value = config.IgnoredFileNameContains.join(';');

        view.querySelector('#chkEnableSeriesAutoDetect').checked = config.AutoDetectSeries;

        view.querySelector('#txtSeriesPattern').value = config.SeriesFolderPattern;

        view.querySelector('#txtDeleteLeftOverFiles').value = config.LeftOverFileExtensionsToDelete.join(';');

        view.querySelector('#txtOverWriteExistingFilesKeyWords').value = config.OverwriteExistingFilesKeyWords ? config.OverwriteExistingFilesKeyWords.join(';') : "";

        view.querySelector('#chkExtendedClean').checked = config.ExtendedClean;

        view.querySelector('#copyOrMoveFile').value = config.CopyOriginalFile.toString();

        view.querySelector('#chkEnableMovieSorting').checked = config.IsMovieSortingEnabled;

        view.querySelector('#chkEnableMoviesAutoDetect').checked = config.AutoDetectMovie;

        view.querySelector('#chkSubMovieFolders').checked = config.CreateMovieInFolder;

        view.querySelector('#txtMovieFolderPattern').value = config.MovieFolderPattern;

        view.querySelector('#txtMoviePattern').value = config.MoviePattern;

        view.querySelector('#selectMovieFolder').value = config.DefaultMovieLibraryPath;


    }

    function onSubmit(view) {

        ApiClient.getNamedConfiguration('autoorganize').then(function (config) {

            //config.EnableScheduledTask = view.querySelector('#chkEnableScheduledTask').checked;

            config.IsEpisodeSortingEnabled = view.querySelector('#chkEnableTvSorting').checked;

            config.IsMovieSortingEnabled = view.querySelector('#chkEnableMovieSorting').checked;

            config.OverwriteExistingFiles = view.querySelector('#chkOverwriteExistingItems').checked;

            config.DeleteEmptyFolders = view.querySelector('#chkDeleteEmptyFolders').checked;

            config.MinFileSizeMb = view.querySelector('#txtMinFileSize').value;

            config.SeasonFolderPattern = view.querySelector('#txtSeasonFolderPattern').value;

            config.SeasonZeroFolderName = view.querySelector('#txtSeasonZeroName').value;

            config.EpisodeNamePattern = view.querySelector('#txtEpisodePattern').value;

            config.MultiEpisodeNamePattern = view.querySelector('#txtMultiEpisodePattern').value;

            config.AutoDetectSeries = view.querySelector('#chkEnableSeriesAutoDetect').checked;

            config.DefaultSeriesLibraryPath = view.querySelector('#selectSeriesFolder').value;

            config.SeriesFolderPattern = view.querySelector('#txtSeriesPattern').value;

            config.DefaultMovieLibraryPath = view.querySelector('#selectMovieFolder').value;

            config.LeftOverFileExtensionsToDelete = view.querySelector('#txtDeleteLeftOverFiles').value.split(';');

            config.IgnoredFileNameContains = view.querySelector('#txtIgnoreFileNameContains').value.split(';');

            config.OverwriteExistingFilesKeyWords = view.querySelector('#txtOverWriteExistingFilesKeyWords').value.split(';');

            config.ExtendedClean = view.querySelector('#chkExtendedClean').checked;

           
            var watchedLocationList = view.querySelector('.watchFolderListContainer');

            var listItems = watchedLocationList.querySelectorAll('.listItem');

            listItems.forEach(item => {
                if(!config.WatchLocations.filter(i => i == item.dataset.folder)) config.WatchLocations.push(item.dataset.folder) 
            })
            

            config.CopyOriginalFile = view.querySelector('#copyOrMoveFile').value;

            config.AutoDetectMovie = view.querySelector('#chkEnableMoviesAutoDetect').checked;

            config.CreateMovieInFolder = view.querySelector('#chkSubMovieFolders').checked;

            config.MovieFolderPattern = view.querySelector('#txtMovieFolderPattern').value;

            config.MoviePattern = view.querySelector('#txtMoviePattern').value;

            ApiClient.updateNamedConfiguration('autoorganize', config).then(Dashboard.processServerConfigurationUpdateResult, Dashboard.processErrorResponse);
        });

        return false;
    }

    function onApiFailure(e) {

        loading.hide();

        require(['alert'], function (alert) {
            alert({
                title: 'Error',
                text: 'Error: ' + e.headers.get("X-Application-Error-Code")
            });
        });
    }

    function getWatchedLocationListItemHtml(watchedLocations) {
        var html = '';
        watchedLocations.forEach(watchedLocation => {
            html += '<div class="listItem listItem-border focusable listItem-hoverable listItem-withContentWrapper" data-action="none" data-folder="' + watchedLocation + '">';
            html += '<div class="listItem-content listItemContent-touchzoom">';
            html += '<div data-action="none" class="listItemImageContainer itemAction listItemImageContainer-square defaultCardBackground defaultCardBackground0" style="aspect-ratio:1">';
            html += '<i class="listItemIcon md-icon">folder</i>';
            html += '</div>';
            html += '<div class="listItemBody itemAction">';
            html += '<div class="listItemBodyText listItemBodyText-nowrap">' + watchedLocation + '</div>';
            html += '</div> ';
            html += '<button title="Delete" aria-label="Delete" type="button" is="paper-icon-button-light" class="listItemButton itemAction paper-icon-button-light icon-button-conditionalfocuscolor" data-action="delete" data-folder="' + watchedLocation + '">';
            html += '<i class="md-icon" style="pointer-events:none">delete</i>';
            html += '</button>';
            html += '</div>';
            html += '</div>';
        })
        return html;
    }

    async function removeWatchedFolder(e, view) {
        var config = await ApiClient.getNamedConfiguration('autoorganize')
        
        var folderToRemove = e.target.dataset.folder;
        var watchLocations = config.WatchLocations.filter(location => location != folderToRemove);
        config.WatchLocations = watchLocations;

        ApiClient.updateNamedConfiguration('autoorganize', config).then(() => {
            Dashboard.processServerConfigurationUpdateResult;
            var watchLocationList = view.querySelector('.watchFolderListContainer');
            watchLocationList.innerHTML = getWatchedLocationListItemHtml(config.WatchLocations);
            var removeButtons = watchLocationList.querySelectorAll('button');
            if (removeButtons) {
                removeButtons.forEach(btn => btn.addEventListener('click', async (elem) => await removeWatchedFolder(elem)))
            }
                
        });
    }

    function getTabs() {
        return [
            {
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeLog'),
                name: globalize.translate("HeaderActivity")
            },
            {
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeSettings'),
                name: globalize.translate("HeaderSettings")
            },
            //{
            //    href: Dashboard.getConfigurationPageUrl('AutoOrganizeMovie'),
            //    name: 'Movie'
            //},
            {
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeSmart'),
                name: 'Smart Matches'
            }];
    }

    return function (view, params) {

        function updateSeriesPatternHelp() {

            var value = view.querySelector('#txtSeriesPattern').value;
            value = getSeriesDirecoryName(value);

            var replacementHtmlResult = 'Result: ' + value;

            view.querySelector('.seriesPatternDescription').innerHTML = replacementHtmlResult;
        }

        function updateSeasonPatternHelp() {

            var value = view.querySelector('#txtSeasonFolderPattern').value;
            value = value.replace('%s', '1').replace('%0s', '01').replace('%00s', '001');

            var replacementHtmlResult = 'Result: ' + value;

            view.querySelector('.seasonFolderFieldDescription').innerHTML = replacementHtmlResult;
        }

        function updateEpisodePatternHelp() {

            var value = view.querySelector('#txtEpisodePattern').value;
            var fileName = getEpisodeFileName(value, false);

            var replacementHtmlResult = 'Result: ' + fileName;

            view.querySelector('.episodePatternDescription').innerHTML = replacementHtmlResult;
        }

        function updateMultiEpisodePatternHelp() {

            var value = view.querySelector('#txtMultiEpisodePattern').value;
            var fileName = getEpisodeFileName(value, true);

            var replacementHtmlResult = 'Result: ' + fileName;

            view.querySelector('.multiEpisodePatternDescription').innerHTML = replacementHtmlResult;
        }

        function selectWatchFolder(e) {

            require(['directorybrowser'], function (directoryBrowser) {

                var picker = new directoryBrowser();

                picker.show({

                    callback: async function (path) {

                        if (path) {
                            var config = await ApiClient.getNamedConfiguration('autoorganize');
                            config.WatchLocations.push(path);
                            ApiClient.updateNamedConfiguration('autoorganize', config).then(() => {
                                Dashboard.processServerConfigurationUpdateResult;
                                var watchLocationList = view.querySelector('.watchFolderListContainer');
                                watchLocationList.innerHTML = getWatchedLocationListItemHtml(config.WatchLocations);
                                watchLocationList.querySelectorAll('Button').forEach(btn => btn.addEventListener('click', async (elem) => await removeWatchedFolder(elem, view)))
                            });
                            
                            //view.querySelector('#txtWatchFolder').value = path;
                        }
                        picker.close();
                    },
                    header: 'Select Watch Folder',
                    validateWriteable: true
                });
            });
        }

        function toggleSeriesLocation() {
            if (view.querySelector('#chkEnableSeriesAutoDetect').checked) {
                view.querySelector('.fldSelectSeriesFolder').classList.remove('hide');
                view.querySelector('#selectSeriesFolder').setAttribute('required', 'required');
            } else {
                view.querySelector('.fldSelectSeriesFolder').classList.add('hide');
                view.querySelector('#selectSeriesFolder').removeAttribute('required');
            }
        }

        function toggleOverwriteExistingItemKeyWords() {
            if (!view.querySelector('#chkOverwriteExistingItems').checked) {
                view.querySelector('.fldOverWriteExistingFilesKeyWords').classList.remove('hide');
                //view.querySelector('#selectSeriesFolder').setAttribute('required', 'required');
            } else {
                view.querySelector('.fldOverWriteExistingFilesKeyWords').classList.add('hide');
                //view.querySelector('#selectSeriesFolder').removeAttribute('required');
            }
        }

        function populateSeriesLocation(config) {

            
            ApiClient.getVirtualFolders().then(function (result) {

                var mediasLocations = [];
                result = result.Items || result;
                for (var n = 0; n < result.length; n++) {

                    var virtualFolder = result[n];

                    for (var i = 0, length = virtualFolder.Locations.length; i < length; i++) {
                        var location = {
                            value: virtualFolder.Locations[i],
                            display: virtualFolder.Name + ': ' + virtualFolder.Locations[i]
                        };

                        if (virtualFolder.CollectionType == 'tvshows') {
                            mediasLocations.push(location);
                        }
                    }
                }

                var mediasFolderHtml = mediasLocations.map(function (s) {
                    return '<option value="' + s.value + '">' + s.display + '</option>';
                }).join('');

                if (mediasLocations.length > 1) {
                    // If the user has multiple folders, add an empty item to enforce a manual selection
                    mediasFolderHtml = '<option value=""></option>' + mediasFolderHtml;
                }

                view.querySelector('#selectSeriesFolder').innerHTML = mediasFolderHtml;

                view.querySelector('#selectSeriesFolder').value = config.DefaultSeriesLibraryPath;

            }, onApiFailure);
        }

        function updateMoviePatternHelp() {

            var value = view.querySelector('#txtMoviePattern').value;
            value = getMovieFileName(value);

            var replacementHtmlResult = 'Result: ' + value;

            view.querySelector('.moviePatternDescription').innerHTML = replacementHtmlResult;
        }

        function updateMovieFolderPatternHelp() {

            var value = view.querySelector('#txtMovieFolderPattern').value;
            value = getMovieFolderFileName(value);

            var replacementHtmlResult = 'Result: ' + value;

            view.querySelector('.movieFolderPatternDescription').innerHTML = replacementHtmlResult;
        }

        function toggleMovieFolderPattern() {
            if (view.querySelector('#chkSubMovieFolders').checked) {
                view.querySelector('.fldSelectMovieFolderPattern').classList.remove('hide');
               
            } else {
                view.querySelector('.fldSelectMovieFolderPattern').classList.add('hide');
                
            }
        }

        function toggleMovieLocation() {
            if (view.querySelector('#chkEnableMoviesAutoDetect').checked) {
                view.querySelector('.fldSelectMovieFolder').classList.remove('hide');
                view.querySelector('#selectMovieFolder').setAttribute('required', 'required');
            } else {
                view.querySelector('.fldSelectMovieFolder').classList.add('hide');
                view.querySelector('#selectMovieFolder').removeAttribute('required');
            }
        }

        function populateMovieLocation(config) {

            ApiClient.getVirtualFolders().then(function (result) {

                var mediasLocations = [];
                result = result.Items || result;
                for (var n = 0; n < result.length; n++) {

                    var virtualFolder = result[n];

                    for (var i = 0, length = virtualFolder.Locations.length; i < length; i++) {
                        var location = {
                            value: virtualFolder.Locations[i],
                            display: virtualFolder.Name + ': ' + virtualFolder.Locations[i]
                        };

                        if (virtualFolder.CollectionType == 'movies') {
                            mediasLocations.push(location);
                        }
                    }
                }

                var mediasFolderHtml = mediasLocations.map(function (s) {
                    return '<option value="' + s.value + '">' + s.display + '</option>';
                }).join('');

                if (mediasLocations.length > 1) {
                    // If the user has multiple folders, add an empty item to enforce a manual selection
                    mediasFolderHtml = '<option value=""></option>' + mediasFolderHtml;
                }

                view.querySelector('#selectMovieFolder').innerHTML = mediasFolderHtml;

                view.querySelector('#selectMovieFolder').value = config.DefaultMovieLibraryPath;

            }, onApiFailure);
        }
           
        view.querySelector('#txtSeriesPattern').addEventListener('change', updateSeriesPatternHelp);
        view.querySelector('#txtSeriesPattern').addEventListener('keyup', updateSeriesPatternHelp);
        view.querySelector('#txtSeasonFolderPattern').addEventListener('change', updateSeasonPatternHelp);
        view.querySelector('#txtSeasonFolderPattern').addEventListener('keyup', updateSeasonPatternHelp);
        view.querySelector('#txtEpisodePattern').addEventListener('change', updateEpisodePatternHelp);
        view.querySelector('#txtEpisodePattern').addEventListener('keyup', updateEpisodePatternHelp);
        view.querySelector('#txtMultiEpisodePattern').addEventListener('change', updateMultiEpisodePatternHelp);
        view.querySelector('#txtMultiEpisodePattern').addEventListener('keyup', updateMultiEpisodePatternHelp);
        
        view.querySelector('#btnSelectWatchFolder').addEventListener('click', selectWatchFolder);
          
        view.querySelector('#chkEnableSeriesAutoDetect').addEventListener('change', () => {
            toggleSeriesLocation();
            onSubmit(view);
            return false;
        });

        view.querySelector('#chkOverwriteExistingItems').addEventListener('change', () => {
            toggleOverwriteExistingItemKeyWords();
            onSubmit(view);
            return false;
        });

        view.querySelector('#txtMoviePattern').addEventListener('change', updateMoviePatternHelp);
        view.querySelector('#txtMoviePattern').addEventListener('keyup', updateMoviePatternHelp);

        view.querySelector('#chkEnableMoviesAutoDetect').addEventListener('change', () => {
            toggleMovieLocation();
            onSubmit(view);
            
        });

        view.querySelector('#chkSubMovieFolders').addEventListener('click', () => {
            toggleMovieFolderPattern();
            onSubmit(view);
            
        });
        view.querySelector('#txtMovieFolderPattern').addEventListener('change', updateMovieFolderPatternHelp);
        view.querySelector('#txtMovieFolderPattern').addEventListener('keyup', updateMovieFolderPatternHelp);


        view.querySelector('.libraryFileOrganizerForm').addEventListener('submit', function (e) {
            e.preventDefault();
            onSubmit(view);
            
        });

        view.addEventListener('viewshow', async function (e) {

            mainTabsManager.setTabs(this, 1, getTabs);

            var config = await ApiClient.getNamedConfiguration('autoorganize')
            
            loadPage(view, config);

            updateSeriesPatternHelp();
            updateSeasonPatternHelp();
            updateEpisodePatternHelp();
            updateMultiEpisodePatternHelp();
            populateSeriesLocation(config);
            toggleSeriesLocation();

            updateMoviePatternHelp();
            updateMovieFolderPatternHelp();
            populateMovieLocation(config);
            toggleMovieLocation();
            toggleMovieFolderPattern();
            
        });
    };
});