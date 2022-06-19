define(['mainTabsManager', 'globalize', 'emby-input', 'emby-select', 'emby-checkbox', 'emby-button', 'emby-collapse', 'emby-toggle', 'dialogHelper'], function (mainTabsManager, globalize, dialogHelper) {
    'use strict';

    ApiClient.getFilePathCorrections = function() {
        var url = this.getUrl("Library/FileOrganizations/FileNameCorrections");
        return this.getJSON(url);
    };

    

    /*
    function openSaveDialog(view, item) {
        var dlg = dialogHelper.createDialog({
            size: "small",
            removeOnClose: !1,
            scrollY: !0
        });

        dlg.classList.add("formDialog");
        dlg.classList.add("ui-body-a");
        dlg.classList.add("background-theme-a");
        dlg.style.maxHeight = "55%";
        dlg.style.maxWidth = "40%";


        var html = '';
        html += '<div class="formDialogHeader" style="display:flex">';
        html += '<button is="paper-icon-button-light" class="btnCloseDialog autoSize paper-icon-button-light" tabindex="-1"><i class="md-icon"></i></button><h3 class="formDialogHeaderTitle">Organize File</h3>';
        html += '</div>';

        html += '<div class="formDialogContent" style="text-align:center; display:flex; justify-content:center;align-items:center">';
        html += '<svg style="width: 55px;height: 55px;top: 19%;position: absolute;" viewBox="0 0 24 24"><path fill="var(--focus-background)" d="M21 11.1V8C21 6.9 20.1 6 19 6H11L9 4H3C1.9 4 1 4.9 1 6V18C1 19.1 1.9 20 3 20H10.2C11.4 21.8 13.6 23 16 23C19.9 23 23 19.9 23 16C23 14.1 22.2 12.4 21 11.1M9.3 18H3V8H19V9.7C18.1 9.2 17.1 9 16 9C12.1 9 9 12.1 9 16C9 16.7 9.1 17.4 9.3 18M16 21C13.2 21 11 18.8 11 16S13.2 11 16 11 21 13.2 21 16 18.8 21 16 21M17 14H15V12H17V14M17 20H15V15H17V20Z"></path></svg>';
        var message = globalize.translate("MessageFollowingFileWillBeMovedFrom") + '<br/><br/>' + item.OriginalPath + '<br/><br/>' + globalize.translate("MessageDestinationTo") + '<br/><br/>' + item.TargetPath;
        if (item.DuplicatePaths.length) {
            message += '<br/><br/>' + 'The following duplicates will be deleted:';

            message += '<br/><br/>' + item.DuplicatePaths.join('<br/>');
        }

        message += '<br/><br/>' + globalize.translate("MessageSureYouWishToProceed");


        html += message;

        html += '<div class="formDialogFooter" >';
        html += '<div style="display:flex;align-items:center;justify-content:center">'
        html += '<button id="okButton" is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem emby-button">Ok</button>';
        html += '<button id="cancelButton" is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem emby-button">Cancel</button>';
        html += '<button id="editButton" is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem emby-button">';
        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24"> ';
        html += '<path fill="white" d="M10 20H6V4H13V9H18V12.1L20 10.1V8L14 2H6C4.9 2 4 2.9 4 4V20C4 21.1 4.9 22 6 22H10V20M20.2 13C20.3 13 20.5 13.1 20.6 13.2L21.9 14.5C22.1 14.7 22.1 15.1 21.9 15.3L20.9 16.3L18.8 14.2L19.8 13.2C19.9 13.1 20 13 20.2 13M20.2 16.9L14.1 23H12V20.9L18.1 14.8L20.2 16.9Z" />';
        html += '</svg> ';
        html += '</button>';
        html += '</div>';
        html += '</div>';


        html += '</div>';

        dlg.innerHTML = html;

        dlg.querySelector('.btnCloseDialog').addEventListener('click',
            () => {
                dialogHelper.close(dlg);
            });

        dlg.querySelector('#cancelButton').addEventListener('click',
            () => {
                dialogHelper.close(dlg);
            })

        dlg.querySelector('#okButton').addEventListener('click',
            () => {
                var options = {
                    RequestToMoveFile: true
                }
                ApiClient.performOrganization(item.Id, options).then(function () {
                    reloadItems(view, false);
                }, reloadItems(view, false));
                dialogHelper.close(dlg);
            });

        dlg.querySelector('#editButton').addEventListener('click', () => {
            showCorrectionPopup(view, item)
            dialogHelper.close(dlg);
        })

        dialogHelper.open(dlg);
    }*/

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

    

    function getEpisodeFileName(value, enableMultiEpisode, delim) {

        var seriesName = "Series Name";
        var episodeTitle = "Episode Four";
        var endingEpisodeTitle = "Episode Five";
        var fileName = seriesName + ' ' + episodeTitle;
        var resolution = "1080p";

        var result = value

        //series number
        result = result.replace('%sn', seriesName)
            .replace('%s.n', seriesName.replaceAll(' ', '.'))
            .replace('%s_n', seriesName.replaceAll(' ', '_'))
            .replace('%s', '1')
            .replace('%0s', '01')
            .replace('%00s', '001')
            .replace('%ext', 'mkv');

        //episode name
        result = result.replace('%en', episodeTitle)
                       .replace('%e.n', episodeTitle.replaceAll(' ', '.'))
                       .replace('%e_n', episodeTitle.replaceAll(' ', '_'))
                       .replace('%fn', fileName)
                       .replace('%res', resolution);

        //multi ep names
        if (enableMultiEpisode) {
            if (result.includes("%e.n")) endingEpisodeTitle = endingEpisodeTitle.replaceAll(' ', '.');
            if (result.includes("%e_n")) endingEpisodeTitle = endingEpisodeTitle.replaceAll(' ', '_');
            result = result.replace('%ed', '5')
                .replace('%0ed', '05')
                .replace('%00ed', '005');
        }

        //episode number
        result = result.replace('%e', '4')
            .replace('%0e', '04')
            .replace('%00e', '004');

        //do this again to sim not using regex
        if (enableMultiEpisode) {
            result = result.replace(episodeTitle, episodeTitle.concat(delim, endingEpisodeTitle));
        }
        return result
    }

    function getSeriesDirectoryName(value) {

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


        view.querySelector('#txtEpisodePattern').value = config.EpisodeNamePattern;

        view.querySelector('#txtMultiEpisodePattern').value = config.MultiEpisodeNamePattern;

        view.querySelector('#txtMultiEpisodeDeliminator').value = config.MultiEpisodeNameDeliminator;

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

            config.MultiEpisodeNameDeliminator = view.querySelector('#txtMultiEpisodeDeliminator').value;

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
            if (episodeKeywordsInput.value == "") {
                config.OverwriteExistingEpisodeFilesKeyWords = [];
            } else {
                config.OverwriteExistingEpisodeFilesKeyWords = episodeKeywordsInput.value.split(';');
            }
            
            var movieKeywordsInput = view.querySelector('#txtOverWriteExistingMovieFilesKeyWords');
            if (movieKeywordsInput.value == "") {
                config.OverwriteExistingMovieFilesKeyWords = [];
            } else {
                config.OverwriteExistingMovieFilesKeyWords = movieKeywordsInput.value.split(';');
            }

            config.ExtendedClean = view.querySelector('#chkExtendedClean').checked;

           
            var watchedLocationList = view.querySelector('.watchFolderListContainer');

            var listItems = watchedLocationList.querySelectorAll('.listItem');

            listItems.forEach(item => {
                if (!config.WatchLocations.filter(i => i == item.dataset.folder))
                    config.WatchLocations.push(item.dataset.folder);
            });
            

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

    return function (view, params) {

        function updateSeriesPatternHelp() {

            var value = view.querySelector('#txtSeriesPattern').value;
            value = getSeriesDirectoryName(value);

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
            var fileName = getEpisodeFileName(value, false, '');

            var replacementHtmlResult = 'Result: ' + fileName;

            view.querySelector('.episodePatternDescription').innerHTML = replacementHtmlResult;
        }

        function updateMultiEpisodePatternHelp() {

            var value = view.querySelector('#txtMultiEpisodePattern').value;
            var delim = view.querySelector('#txtMultiEpisodeDeliminator').value;
            var fileName = getEpisodeFileName(value, true, delim);

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

        //function toggleSeriesLocation() {
        //    if (view.querySelector('#chkEnableSeriesAutoDetect').checked) {
        //        view.querySelector('.fldSelectSeriesFolder').classList.remove('hide');
        //        view.querySelector('#selectSeriesFolder').setAttribute('required', 'required');
        //    } else {
        //        view.querySelector('.fldSelectSeriesFolder').classList.add('hide');
        //        view.querySelector('#selectSeriesFolder').removeAttribute('required');
        //    }
        //}

        function toggleTelevisionOptions()
        {
            if (view.querySelector('#chkEnableTelevisionOptions').checked) {
                view.querySelector('.televisionOptions').classList.remove('hide');
                view.querySelector('#selectSeriesFolder').required = true;
            } else {
                view.querySelector('.televisionOptions').classList.add('hide');
                view.querySelector('#selectSeriesFolder').required = false;
            }
        }

        function toggleMovieOptions()
        {
            if (view.querySelector('#chkEnableMovieOptions').checked) {
                view.querySelector('.movieOptions').classList.remove('hide');
                view.querySelector('#selectMovieFolder').required = true;
            } else {
                view.querySelector('.movieOptions').classList.add('hide');
                view.querySelector('#selectMovieFolder').required = false;
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

        //function toggleMovieLocation() {
        //    if (view.querySelector('#chkEnableMoviesAutoDetect').checked) {
        //        view.querySelector('.fldSelectMovieFolder').classList.remove('hide');
        //        view.querySelector('#selectMovieFolder').setAttribute('required', 'required');
        //    } else {
        //        view.querySelector('.fldSelectMovieFolder').classList.add('hide');
        //        view.querySelector('#selectMovieFolder').removeAttribute('required');
        //    }
        //}

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
        view.querySelector('#txtMultiEpisodeDeliminator').addEventListener('change', updateMultiEpisodePatternHelp);
        view.querySelector('#txtMultiEpisodeDeliminator').addEventListener('keyup', updateMultiEpisodePatternHelp);
        
        view.querySelector('#btnSelectWatchFolder').addEventListener('click', selectWatchFolder);
          
        //view.querySelector('#chkEnableSeriesAutoDetect').addEventListener('change', () => {
        //    //toggleSeriesLocation();
        //    onSubmit(view);
        //    return false;
        //});

        view.querySelector('#chkEnableTelevisionOptions').addEventListener('change', () => {
            toggleTelevisionOptions();
            return false;
        });
        view.querySelector('#chkEnableMovieOptions').addEventListener('change', () => {
            toggleMovieOptions();
            return false;
        });
        view.querySelector('#chkOverwriteExistingEpisodeItems').addEventListener('change', () => {
            toggleOverwriteExistingEpisodeItemKeyWords();
            return false;
        });

        view.querySelector('#chkOverwriteExistingMovieItems').addEventListener('change', () => {
            toggleOverwriteExistingMovieItemKeyWords();
            return false;
        });

        view.querySelector('#txtMoviePattern').addEventListener('change', updateMoviePatternHelp);
        view.querySelector('#txtMoviePattern').addEventListener('keyup', updateMoviePatternHelp);

        view.querySelector('#chkEnableSeriesAutoDetect').addEventListener('change', () => {
            toggleSortExistingSeriesOnly();
        });

        view.querySelector('#chkSubMovieFolders').addEventListener('click', () => {
            toggleMovieFolderPattern();
        });
        view.querySelector('#txtMovieFolderPattern').addEventListener('change', updateMovieFolderPatternHelp);
        view.querySelector('#txtMovieFolderPattern').addEventListener('keyup', updateMovieFolderPatternHelp);


        view.querySelector('.libraryFileOrganizerForm').addEventListener('submit', function (e) {
            e.preventDefault();
            onSubmit(view);
        });

        view.addEventListener('viewshow', async function (e) {

            const correction = await ApiClient.getFilePathCorrections();
            addCorrectionsTab = correction.Items.length > 0;
            mainTabsManager.setTabs(this, 1, getTabs);

            var config = await ApiClient.getNamedConfiguration('autoorganize');
            
            loadPage(view, config);

            updateSeriesPatternHelp();
            updateSeasonPatternHelp();
            updateEpisodePatternHelp();
            updateMultiEpisodePatternHelp();
            populateSeriesLocation(config);
            //toggleSeriesLocation();

            updateMoviePatternHelp();
            updateMovieFolderPatternHelp();
            populateMovieLocation(config);
            //toggleMovieLocation();
            toggleMovieFolderPattern();
            toggleSortExistingSeriesOnly();
            toggleMovieOptions();
            toggleTelevisionOptions();
        });
    };
});