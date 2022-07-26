define(['mainTabsManager', 'globalize','loading', 'emby-input', 'emby-select', 'emby-checkbox', 'emby-button', 'emby-collapse', 'emby-toggle'], function (mainTabsManager, globalize, loading) {
    
    ApiClient.getFilePathCorrections = function() {
        const url = this.getUrl("Library/FileOrganizations/FileNameCorrections");
        return this.getJSON(url);
    };

    ApiClient.getAvailableSpace = function(drive) {
        
        const options = {
            Location: drive
        };
        const url = this.getUrl("AutoOrganize/AvailableSpace", options);
        return this.getJSON(url);
    }
    
    //https://stackoverflow.com/questions/15900485/correct-way-to-convert-size-in-bytes-to-kb-mb-gb-in-javascript
    function formatBytes(bytes, decimals = 2) {
        if (bytes === 0) return '0 Bytes';

        const k = 1024;
        const dm = decimals < 0 ? 0 : decimals;
        const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB'];

        const i = Math.floor(Math.log(bytes) / Math.log(k));

        return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
    }
    
    function getMovieFileName(value) {
        const movieName = "Movie Name";
        const movieYear = "2017";
        const fileNameWithoutExt = movieName + '.' + movieYear + '.MULTI.1080p.BluRay.Directors.Cut.DTS.x264-UTT';

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
        const movieName = "Movie Name";
        const movieYear = "2017";
        const fileNameWithoutExt = movieName + '.' + movieYear + '.MULTI.1080p.BluRay.Directors.Cut.DTS.x264-UTT';

        var result = value.replace('%mn', movieName)
            .replace('%m.n', movieName.replace(' ', '.'))
            .replace('%m_n', movieName.replace(' ', '_'))
            .replace('%my', movieYear)
            .replace('%ext', 'mkv')
            .replace('%fn', fileNameWithoutExt);

        return result;
    }
     
    function getEpisodeFileName(value, enableMultiEpisode) {

        const seriesName = "Series Name";
        const episodeTitle = enableMultiEpisode ? "Episode Four, Episode Five" : "Episode Four";
        const fileName = seriesName + ' ' + episodeTitle;
        const resolution = "1080p";

        var result = value.replace('%sn', seriesName)
            .replace('%s.n', seriesName.replace(' ', '.'))
            .replace('%s_n', seriesName.replace(' ', '_'))
            .replace('%s', '1')
            .replace('%0s', '01')
            .replace('%00s', '001')
            .replace('%ext', 'mkv')
            .replace('%en', episodeTitle)
            .replace('%e.n', episodeTitle.replaceAll('Episode ', 'Episode.'))
            .replace('%e_n', episodeTitle.replaceAll('Episode ', 'Episode_'))
            .replace('%fn', fileName)
            .replace('%res', resolution);

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

    function getSeriesDirectoryName(value) {

        const seriesName = "Series Name";
        const seriesYear = "2017";
        const fullName = seriesName + ' (' + seriesYear + ')';

        return value.replace('%sn', seriesName)
            .replace('%s.n', seriesName.replace(' ', '.'))
            .replace('%s_n', seriesName.replace(' ', '_'))
            .replace('%sy', seriesYear)
            .replace('%fn', fullName);
    }

    async function loadPage(view, config) {

        
        view.querySelector('#chkEnableTelevisionOptions').checked = config.EnableTelevisionOrganization;

        view.querySelector('#chkEnableMovieOptions').checked = config.EnableMovieOrganization;

        view.querySelector('#chkEnableSubtitleSorting').checked = config.EnableSubtitleOrganization;

        view.querySelector('#chkOverwriteExistingEpisodeItems').checked = config.OverwriteExistingEpisodeFiles;

        view.querySelector('#chkOverwriteExistingMovieItems').checked = config.OverwriteExistingMovieFiles;

        view.querySelector('#chkDeleteEmptyFolders').checked = config.DeleteEmptyFolders;

        view.querySelector('#txtMinFileSize').value = config.MinFileSizeMb;

        view.querySelector('#txtSeasonFolderPattern').value = config.SeasonFolderPattern;

        view.querySelector('#txtSeasonZeroName').value = config.SeasonZeroFolderName;

        view.querySelector('#chkEnableSubtitleSorting').checked = config.EnableSubtitleOrganization;

       
        var watchLocationList = view.querySelector('.watchFolderListContainer');
        watchLocationList.innerHTML = getWatchedLocationListItemHtml(config.WatchLocations);
        var removeButtons = watchLocationList.querySelectorAll('Button');
        if (removeButtons) {
            removeButtons.forEach(btn => btn.addEventListener('click',
                async (e) => await removeWatchedFolder(e, view)));
        }

        view.querySelector('#chkEnablePreProcessingOptions').checked = config.EnablePreProcessing;

        view.querySelector('#txtPreProcessingFolderPath').value = config.PreProcessingFolderPath || "";

        view.querySelector('#txtEpisodePattern').value = config.EpisodeNamePattern;

        view.querySelector('#txtMultiEpisodePattern').value = config.MultiEpisodeNamePattern;

        view.querySelector('#txtIgnoreFileNameContains').value = config.IgnoredFileNameContains.join(';');

        view.querySelector('#chkEnableSeriesAutoDetect').checked = config.AutoDetectSeries;

        view.querySelector('#chkSortExistingSeriesOnly').checked = config.SortExistingSeriesOnly;

        view.querySelector('#txtSeriesPattern').value = config.SeriesFolderPattern;

        view.querySelector('#txtDeleteLeftOverFiles').value = config.LeftOverFileExtensionsToDelete.join(';');

        view.querySelector('#txtOverWriteExistingEpisodeFilesKeyWords').value = config.OverwriteExistingEpisodeFilesKeyWords ? config.OverwriteExistingEpisodeFilesKeyWords.join(';') : "";

        view.querySelector('#txtOverWriteExistingMovieFilesKeyWords').value = config.OverwriteExistingMovieFilesKeyWords ? config.OverwriteExistingMovieFilesKeyWords.join(';') : "";

        view.querySelector('#chkExtendedClean').checked = config.ExtendedClean;

        view.querySelector('#copyOrMoveFile').value = config.CopyOriginalFile.toString();

        view.querySelector('#chkEnableMoviesAutoDetect').checked = config.AutoDetectMovie;

        view.querySelector('#chkSubMovieFolders').checked = config.CreateMovieInFolder;

        view.querySelector('#txtMovieFolderPattern').value = config.MovieFolderPattern;

        view.querySelector('#txtMoviePattern').value = config.MoviePattern;

        view.querySelector('#selectMovieFolder').value = config.DefaultMovieLibraryPath;

    }

    function onSubmit(view) {

        ApiClient.getNamedConfiguration('autoorganize').then(function (config) {

            config.EnableTelevisionOrganization = view.querySelector('#chkEnableTelevisionOptions').checked ;
            
            config.EnableMovieOrganization = view.querySelector('#chkEnableMovieOptions').checked;
            
            config.AutoDetectMovie = view.querySelector('#chkEnableMoviesAutoDetect').checked;

            config.OverwriteExistingEpisodeFiles = view.querySelector('#chkOverwriteExistingEpisodeItems').checked;

            config.OverwriteExistingMovieFiles = view.querySelector('#chkOverwriteExistingMovieItems').checked;

            config.DeleteEmptyFolders = view.querySelector('#chkDeleteEmptyFolders').checked;

            config.MinFileSizeMb = view.querySelector('#txtMinFileSize').value;

            config.SeasonFolderPattern = view.querySelector('#txtSeasonFolderPattern').value;

            config.SeasonZeroFolderName = view.querySelector('#txtSeasonZeroName').value;

            config.EpisodeNamePattern = view.querySelector('#txtEpisodePattern').value;

            config.MultiEpisodeNamePattern = view.querySelector('#txtMultiEpisodePattern').value;

            config.AutoDetectSeries = view.querySelector('#chkEnableSeriesAutoDetect').checked;

            //Only set this value to it checked state if the use has enabled auto sorting
            if (config.AutoDetectSeries) {
                config.SortExistingSeriesOnly = view.querySelector('#chkSortExistingSeriesOnly').checked;
            } else {
                config.SortExistingSeriesOnly = false;
            }

            config.EnableSubtitleOrganization = view.querySelector('#chkEnableSubtitleSorting').checked;

            config.DefaultSeriesLibraryPath = view.querySelector('#selectSeriesFolder').value;

            config.SeriesFolderPattern = view.querySelector('#txtSeriesPattern').value;

            config.DefaultMovieLibraryPath = view.querySelector('#selectMovieFolder').value;

            config.LeftOverFileExtensionsToDelete = view.querySelector('#txtDeleteLeftOverFiles').value.split(';');

            config.IgnoredFileNameContains = view.querySelector('#txtIgnoreFileNameContains').value.split(';');

            var episodeKeywordsInput = view.querySelector('#txtOverWriteExistingEpisodeFilesKeyWords');
            if (episodeKeywordsInput.value === "") {
                config.OverwriteExistingEpisodeFilesKeyWords = [];
            } else {
                config.OverwriteExistingEpisodeFilesKeyWords = episodeKeywordsInput.value.split(';');
            }
            
            var movieKeywordsInput = view.querySelector('#txtOverWriteExistingMovieFilesKeyWords');
            if (movieKeywordsInput.value === "") {
                config.OverwriteExistingMovieFilesKeyWords = [];
            } else {
                config.OverwriteExistingMovieFilesKeyWords = movieKeywordsInput.value.split(';');
            }

            config.ExtendedClean = view.querySelector('#chkExtendedClean').checked;

           
            var watchedLocationList = view.querySelector('.watchFolderListContainer');

            var listItems = watchedLocationList.querySelectorAll('.listItem');

            listItems.forEach(item => {
                if (!config.WatchLocations.filter(i => i === item.dataset.folder))
                    config.WatchLocations.push(item.dataset.folder);
            });
            

            config.CopyOriginalFile = view.querySelector('#copyOrMoveFile').value;

            config.AutoDetectMovie = view.querySelector('#chkEnableMoviesAutoDetect').checked;

            config.CreateMovieInFolder = view.querySelector('#chkSubMovieFolders').checked;

            config.MovieFolderPattern = view.querySelector('#txtMovieFolderPattern').value;

            config.MoviePattern = view.querySelector('#txtMoviePattern').value;

            config.EnablePreProcessing = view.querySelector('#chkEnablePreProcessingOptions').checked;

            config.PreProcessingFolderPath = view.querySelector('#txtPreProcessingFolderPath').value;
            
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
        });
        return html;
    }

    async function removeWatchedFolder(e, view) {
        var config = await ApiClient.getNamedConfiguration('autoorganize');

        var folderToRemove = e.target.dataset.folder;
        const watchLocations = config.WatchLocations.filter(location => location !== folderToRemove);
        config.WatchLocations = watchLocations;

        ApiClient.updateNamedConfiguration('autoorganize', config).then(() => {
            Dashboard.processServerConfigurationUpdateResult;
            var watchLocationList = view.querySelector('.watchFolderListContainer');
            watchLocationList.innerHTML = getWatchedLocationListItemHtml(config.WatchLocations);
            var removeButtons = watchLocationList.querySelectorAll('button');
            if (removeButtons) {
                removeButtons.forEach(btn => btn.addEventListener('click', async (elem) => await removeWatchedFolder(elem)));
            }
                
        });
    }

    var addCorrectionsTab = false;
    function getTabs() {
        var tabs = [
            {
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeLog'),
                name: globalize.translate("HeaderActivity")
            },
            {
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeSettings'),
                name: globalize.translate("HeaderSettings")
            },
            {
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeSmart'),
                name: 'Smart Matches'
            }
        ];
        
        if (addCorrectionsTab) {
            tabs.push({
                href: Dashboard.getConfigurationPageUrl('AutoOrganizeCorrections'),
                name: 'Corrections'
            });
        }
        return tabs;
    }

    return function (view) {

        function updateSeriesPatternHelp() {

            var value = view.querySelector('#txtSeriesPattern').value;
            value = getSeriesDirectoryName(value);

            const replacementHtmlResult = 'Result: ' + value;

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

        function togglePreProcessingOptions() {
           
            const preProcessingFolderPathContainer = view.querySelector('#txtPreProcessingFolderPath').closest('.inputContainer');
            if (view.querySelector('#chkEnablePreProcessingOptions').checked) {
                preProcessingFolderPathContainer.classList.remove('hide');
                view.querySelector('#txtPreProcessingFolderPath').setAttribute("required", "");
                //When preprocessing is selected, the file will be moved from the preprocessing folder into the library. Just hide the Copy/Move Option in the settings
                view.querySelector('#copyOrMoveFile').closest('.selectContainer').classList.add('hide');
            } else {
                preProcessingFolderPathContainer.classList.add('hide');
                view.querySelector('#txtPreProcessingFolderPath').removeAttribute("required");
                view.querySelector('#copyOrMoveFile').closest('.selectContainer').classList.remove('hide');
            }
        }

        function selectPreProcessingFolder() {
            require(['directorybrowser'], function (DirectoryBrowser) {

                var picker = new DirectoryBrowser();

                picker.show({

                    callback: async function (path) {

                        if (path) {
                            //var config = await ApiClient.getNamedConfiguration('autoorganize');
                            //config.PreProcessingFolderPath = path;
                            //ApiClient.updateNamedConfiguration('autoorganize', config).then(() => {
                            //    Dashboard.processServerConfigurationUpdateResult;
                            //    var preProcessingFolder = view.querySelector('#txtPreProcessingFolderPath');
                            //    preProcessingFolder.value = config.PreProcessingFolderPath;
                            //});
                            view.querySelector('#txtPreProcessingFolderPath').value = path;
                        }
                        picker.close();
                    },
                    header: 'Select Pre-processing Folder',
                    validateWriteable: true
                });
            });
        }
        
        function selectWatchFolder() {

            require(['directorybrowser'], function (DirectoryBrowser) {

                var picker = new DirectoryBrowser();

                picker.show({

                    callback: async function (path) {

                        if (path) {
                            var config = await ApiClient.getNamedConfiguration('autoorganize');
                            config.WatchLocations.push(path);
                            ApiClient.updateNamedConfiguration('autoorganize', config).then(() => {
                                Dashboard.processServerConfigurationUpdateResult;
                                var watchLocationList = view.querySelector('.watchFolderListContainer');
                                watchLocationList.innerHTML = getWatchedLocationListItemHtml(config.WatchLocations);
                                watchLocationList.querySelectorAll('Button').forEach(btn => btn.addEventListener('click', async (elem) => await removeWatchedFolder(elem, view)));
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

        function toggleTelevisionOptions()
        {
            if (view.querySelector('#chkEnableTelevisionOptions').checked) {
                view.querySelector('.televisionOptions').classList.remove('hide');
            } else {
                view.querySelector('.televisionOptions').classList.add('hide');
            }
        }

        function toggleMovieOptions()
        {
            if (view.querySelector('#chkEnableMovieOptions').checked) {
                view.querySelector('.movieOptions').classList.remove('hide');
            } else {
                view.querySelector('.movieOptions').classList.add('hide');
            }
        }

        function toggleSortExistingSeriesOnly() {
            if (view.querySelector('#chkEnableSeriesAutoDetect').checked) {
                view.querySelector('.fldSortExistingSeriesOnly').classList.remove('hide');
            } else {
                view.querySelector('.fldSortExistingSeriesOnly').classList.add('hide');
            }
        }

        function toggleOverwriteExistingEpisodeItemKeyWords() {
            if (!view.querySelector('#chkOverwriteExistingEpisodeItems').checked) {
                view.querySelector('.fldOverWriteExistingEpisodeFilesKeyWords').classList.remove('hide');
            } else {
                view.querySelector('.fldOverWriteExistingEpisodeFilesKeyWords').classList.add('hide');
            }
        }

        function toggleOverwriteExistingMovieItemKeyWords() {
            if (!view.querySelector('#chkOverwriteExistingMovieItems').checked) {
                
                view.querySelector('.fldOverWriteExistingMovieFilesKeyWords').classList.remove('hide');
            } else {
                
                view.querySelector('.fldOverWriteExistingMovieFilesKeyWords').classList.add('hide');
            }
        }

        async function populateSeriesLocation(config) {

            var result = await ApiClient.getVirtualFolders();

            var mediasLocations = [];
            result = result.Items || result;
            for (var n = 0; n < result.length; n++) {

                var virtualFolder = result[n];

               
                
                for (var i = 0, length = virtualFolder.Locations.length; i < length; i++) {
                    var availableSpace = await ApiClient.getAvailableSpace(virtualFolder.Locations[i]); 
                    var location = {
                        value: virtualFolder.Locations[i],
                        display: (availableSpace > 0 ? '(Available space: ' + formatBytes(availableSpace)  + ') ' : '') + virtualFolder.Name + ': ' + virtualFolder.Locations[i]
                    };

                    if (virtualFolder.CollectionType === 'tvshows') {
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

        async function populateMovieLocation(config) {

            var result = await ApiClient.getVirtualFolders();

            var mediasLocations = [];
            result = result.Items || result;
            for (let n = 0; n < result.length; n++) {

                const virtualFolder = result[n];
               
                for (var i = 0, length = virtualFolder.Locations.length; i < length; i++) {
                    
                    var availableSpace = await ApiClient.getAvailableSpace(virtualFolder.Locations[i]);                    
                    var location = {
                        value: virtualFolder.Locations[i],
                        display: (availableSpace > 0 ? '(Available space: ' + formatBytes(availableSpace) + ') ' : '') + virtualFolder.Name + ': ' + virtualFolder.Locations[i] 
                    };

                    if (virtualFolder.CollectionType === 'movies') {
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


        view.querySelector('#chkEnableTelevisionOptions').addEventListener('change', () => {
            toggleTelevisionOptions();
            onSubmit(view);
            return false;
        });
        
        view.querySelector('#chkEnableMovieOptions').addEventListener('change', () => {
            toggleMovieOptions();
            onSubmit(view);
            return false;
        });
        
        view.querySelector('#chkOverwriteExistingEpisodeItems').addEventListener('change', () => {
            toggleOverwriteExistingEpisodeItemKeyWords();
            onSubmit(view);
            return false;
        });

        view.querySelector('#chkOverwriteExistingMovieItems').addEventListener('change', () => {
            toggleOverwriteExistingMovieItemKeyWords();
            onSubmit(view);
            return false;
        });

        view.querySelector('#chkEnablePreProcessingOptions').addEventListener('change', () => {
            togglePreProcessingOptions();
            return false;
        });
        
        view.querySelector('#btnSelectPreProcessingFolderPath').addEventListener('click', selectPreProcessingFolder);
        
        view.querySelector('#txtMoviePattern').addEventListener('change', updateMoviePatternHelp);
        view.querySelector('#txtMoviePattern').addEventListener('keyup', updateMoviePatternHelp);

        view.querySelector('#chkEnableSeriesAutoDetect').addEventListener('change', () => {
            
            toggleSortExistingSeriesOnly();

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

        view.addEventListener('viewshow', async function () {

            loading.show();
            //Figure out if we should show the corrections tab
            const correction = await ApiClient.getFilePathCorrections();
            addCorrectionsTab = correction.Items.length > 0;
            
            mainTabsManager.setTabs(this, 1, getTabs);

            const config = await ApiClient.getNamedConfiguration('autoorganize');
            
            await loadPage(view, config);

            updateSeriesPatternHelp();
            updateSeasonPatternHelp();
            updateEpisodePatternHelp();
            updateMultiEpisodePatternHelp();
            await populateSeriesLocation(config);
            updateMoviePatternHelp();
            updateMovieFolderPatternHelp();
            await populateMovieLocation(config); 
            toggleMovieFolderPattern();
            toggleSortExistingSeriesOnly();
            toggleMovieOptions();
            toggleTelevisionOptions();
            togglePreProcessingOptions();

            loading.hide();
        });
    };
});