define(['globalize', 'serverNotifications', 'events', 'datetime', 'loading', 'mainTabsManager', 'dialogHelper', 'paper-icon-button-light', 'formDialogStyle','emby-linkbutton', 'emby-collapse', 'emby-input'], function (globalize, serverNotifications, events, datetime, loading, mainTabsManager, dialogHelper) {
    
    ApiClient.getFilePathCorrections = function() {
        var url = this.getUrl("Library/FileOrganizations/FileNameCorrections");
        return this.getJSON(url);
    };

    ApiClient.getScheduledTask = function (options) {
        var url = this.getUrl("ScheduledTasks?IsHidden=false&IsEnabled=true", options || {});
        return this.getJSON(url);
    };

    ApiClient.getFileOrganizationResults = function (options) {

        try {
            var url = this.getUrl("Library/FileOrganization", options || {});

            return this.getJSON(url);
        } catch (err) {
            return "";
        }
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

    ApiClient.clearOrganizationCompletedLog = function () {

        var url = this.getUrl("Library/FileOrganizations/Completed");

        return this.ajax({
            type: "DELETE",
            url: url
        });
    };

    ApiClient.performOrganization = function (id, options) {

        //Only one option: RequestToMoveFile = true
        var url = this.getUrl("Library/FileOrganizations/" + id + "/Organize");

        return this.ajax({
            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: 'application/json'
        });
    };
    //
    //ApiClient.performEpisodeOrganization = function (id, options) {

    //    var url = this.getUrl("Library/FileOrganizations/" + id + "/Episode/Organize");

    //    return this.ajax({
    //        type: "POST",
    //        url: url,
    //        data: JSON.stringify(options),
    //        contentType: 'application/json'
    //    });
    //};

    //ApiClient.performMovieOrganization = function (id, options) {

    //    var url = this.getUrl("Library/FileOrganizations/" + id + "/Movie/Organize");

    //    return this.ajax({
    //        type: "POST",
    //        url: url,
    //        data: JSON.stringify(options),
    //        contentType: 'application/json'
    //    });
    //};
    
    ApiClient.getRemoteSearchImages = async function (options, item) {

        var url = this.getUrl("Items/RemoteSearch/" + item.Type);
        
        var result = await this.ajax({

            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: "application/json"
        });
        console.log(result)
        return result;
    };

    

    var query = {
        StartIndex: 0,
        Limit: 50, 
        Type: 'All',
        Ascending : false,
        SortBy: 'OrganizationDate'
    };

    var currentResult;
    var pageGlobal;
   
    //https://stackoverflow.com/questions/15900485/correct-way-to-convert-size-in-bytes-to-kb-mb-gb-in-javascript
    function formatBytes(bytes, decimals = 2) {
        if (bytes === 0) return '0 Bytes';

        const k = 1024;
        const dm = decimals < 0 ? 0 : decimals;
        const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB'];

        const i = Math.floor(Math.log(bytes) / Math.log(k));

        return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
    }

    function capitalizeTheFirstLetterOfEachWord(words) {
        try {
            var separateWord = words.toLowerCase().split(' ');
            for (var i = 0; i < separateWord.length; i++) {
                separateWord[i] = separateWord[i].charAt(0).toUpperCase() +
                    separateWord[i].substring(1);
            }
            return separateWord.join(' ');
        } catch (err) {
            return words
        }
    }

    function formatItemName(file_name) {

        try { //If a subtitle file makes it through during scanning process, we'll throw an error here. Could happen.
            file_name = file_name.split(".").join(" ");
        } catch (err) {

        }

        file_name = capitalizeTheFirstLetterOfEachWord(file_name)
        return file_name;
    }

    function parentWithClass(elem, className) {

        while (!elem.classList || !elem.classList.contains(className)) {
            elem = elem.parentNode;

            if (!elem) {
                return null;
            }
        }

        return elem;
    }

    function deleteOriginalFile(page, id) {

        var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];

        var message = 'The following file will be deleted:' + '<br/><br/>' + item.OriginalPath + '<br/><br/>' + 'Are you sure you wish to proceed?';

        require(['confirm'], function (confirm) {

            confirm(message, 'Delete File').then(function () {

                loading.show();

                ApiClient.deleteOriginalFileFromOrganizationResult(id).then(async function () {

                    loading.hide();

                    await reloadItems(page, true);

                }, Dashboard.processErrorResponse);
            });
        });
    }
    
    function showCorrectionPopup(page, item) {

        require([Dashboard.getConfigurationResourceUrl('FileOrganizerJs')], async function (fileorganizer) {

            await fileorganizer.show(item).then(async function () {
                await reloadItems(page, false);
            }, function () { /* Do nothing on reject */ });
        });
    }

    function organizeFile(page, id) {

        var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];

        
        if (!item.TargetPath) {

            return;

        }

        openConfirmDialog(page, item);
       
    }

    function openConfirmDialog(view, item) {
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

        html += '<div class="formDialogContent" style="text-align:center; display:flex; justify-content:center;align-items:center;margin: 2em;">';
        html += '<svg style="width: 55px;height: 55px;top: 19%;position: absolute;" viewBox="0 0 24 24"><path fill="var(--focus-background)" d="M21 11.1V8C21 6.9 20.1 6 19 6H11L9 4H3C1.9 4 1 4.9 1 6V18C1 19.1 1.9 20 3 20H10.2C11.4 21.8 13.6 23 16 23C19.9 23 23 19.9 23 16C23 14.1 22.2 12.4 21 11.1M9.3 18H3V8H19V9.7C18.1 9.2 17.1 9 16 9C12.1 9 9 12.1 9 16C9 16.7 9.1 17.4 9.3 18M16 21C13.2 21 11 18.8 11 16S13.2 11 16 11 21 13.2 21 16 18.8 21 16 21M17 14H15V12H17V14M17 20H15V15H17V20Z"></path></svg>';
        var message = globalize.translate("MessageFollowingFileWillBeMovedFrom") + '<br/><br/>' + item.OriginalPath.replaceAll(".", "<wbr>.") + '<br/><br/>' + globalize.translate("MessageDestinationTo") + '<br/><br/>' + item.TargetPath;
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
                ApiClient.performOrganization(item.Id, options).then(async function () {
                    await reloadItems(view, false);
                }, function() {});
                dialogHelper.close(dlg);
            });
       
        dlg.querySelector('#editButton').addEventListener('click', () => {
            showCorrectionPopup(view, item)
            dialogHelper.close(dlg);
        })

        dialogHelper.open(dlg);
    }

    async function openComparisonDialog(id) {
       
        var sourceResult = await ApiClient.getFileOrganizationResults(query)
        var item = sourceResult.Items.filter(r => r.Id == id)[0];
        var libraryResult = await ApiClient.getJSON( await ApiClient.getUrl('Items?Recursive=true&Fields=MediaStreams&IncludeItemTypes=' + item.Type + '&Ids=' + item.ExistingInternalId))
        
        var libraryItem = libraryResult.Items[0];
        if (!libraryItem) return;


        var libraryItemResolution = parseInt(libraryItem.MediaSources[0].MediaStreams.filter(s => s.Type === "Video")[0].DisplayTitle.split(' ')[0].replace('p', ''))
        var sourceResolution = parseInt(item.ExtractedResolution.Name.replace('p', ''));


        var dlg = dialogHelper.createDialog({
            size: "small",
            removeOnClose: !1,
            scrollY: !0
        });

        dlg.classList.add("formDialog");
        dlg.classList.add("ui-body-a");
        dlg.classList.add("background-theme-a");
        dlg.style.height = "20em";


        var html = '';
        html += '<div class="formDialogHeader" style="display:flex">';
        html += '<button is="paper-icon-button-light" class="btnCloseDialog autoSize paper-icon-button-light" tabindex="-1"><i class="md-icon"></i></button>';
        
        if (item.Type === "Episode") {
            html += '<h3 class="formDialogHeaderTitle">' + libraryItem.SeriesName + ' Season ' + libraryItem.ParentIndexNumber + ' Episode ' + libraryItem.IndexNumber + '</h3>';
        }
       
        if (item.Type === "Movie") {
            html += '<h3 class="formDialogHeaderTitle">' + libraryItem.Name + '</h3>';
        }

        html += '</div>';

        html += '<div class="formDialogContent" style="margin:2em;">';

        html += '<table class="tblOrganizationResults table detailTable ui-responsive" style="width:100%">'
        html += '<thead> ';
        html += '<tr style="text-align: left;">';
        
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="3">Location</th>'
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="3">File Size</th>'
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="1">Release/<br>Edition</th>'
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="1">Quality</th>'
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="1"></th> '   //Quality is up or down
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="1">Codec</th> '
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="1">Audio</th>'
        //html += '<th class="detailTableHeaderCell" data-priority="1">Subtitles</th>'
        html += '<th style="border-spacing: 0; padding: .4em; font-weight: 700; text-align: left; vertical-align: top" data-priority="1">Action</th>'
        html += '</tr>';
        html += '</thead>';
        html += '<tbody class="resultBody">';

        //Source Folder Item
        html += '<tr class="detailTableBodyRow detailTableBodyRow-shaded">';
        
        html += '<td class="detailTableBodyCell fileCell">';
        html += 'Source Folder'
        html += '</td>';

        //File Size
        html += '<td style="border-spacing:0;padding:.4em" data-title="File Size">';
        html += '<span>' + formatBytes(item.FileSize) + '</span>';
        html += '</td>';

        //Release Edition
        html += '<td style="border-spacing:0;padding:.4em" data-title="Edition">';
        switch(item.Type) {
        case "Episode":
            if (item.ExtractedSeasonNumber && item.ExtractedEpisodeNumber) {
                html += '<span>' + item.ExtractedSeasonNumber + 'x' + (item.ExtractedEpisodeNumber <= 9 ? `0${item.ExtractedEpisodeNumber}` : item.ExtractedEpisodeNumber) + '</span>';
            } else {
                html += '';
            }
        case "Movie":
            html += '<span>' + item.ExtractedEdition ?? "" + '</span>';  
        }
        html += '</td>';

        //Quality
        html += '<td style="border-spacing:0;padding:.4em" data-title="Resolution">';
        html += '<span style="color: white;background-color: rgb(131,131,131); padding: 5px 10px 5px 10px;border-radius: 5px;font-size: 11px;">' + item.ExtractedResolution.Name ?? ""  + '</span>';  
        html += '</td>';

        //Quality is up or down
        html += '<td style="border-spacing:0;padding:.4em" data-title="Resolution">';
        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';

        if (sourceResolution == libraryItemResolution) {
            html += '<path fill="green" d="M19,10H5V8H19V10M19,16H5V14H19V16Z" />';
        }
        else if (sourceResolution > libraryItemResolution) {
            html += '<path fill="green" d="M7.03 9.97H11.03V18.89L13.04 18.92V9.97H17.03L12.03 4.97Z" />';
        } else {
            html += ' <path fill="red" d="M7.03 13.92H11.03V5L13.04 4.97V13.92H17.03L12.03 18.92Z" />';
        }
        html += '</svg>';  
        html += '</td>';

        //Codec
        html += '<td style="border-spacing:0;padding:.4em" data-title="Codec">';
        if (item.VideoStreamCodecs.length) {
            html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 10px 1px 10px;border-radius: 5px;margin:2px;font-size: 11px; text-align:center">' + item.VideoStreamCodecs[0] + '</span>';
        }
        html += '</td>';

        //Audio
        html += '<td style="border-spacing:0;padding:.4em" data-title="Audio">';
        if (item.AudioStreamCodecs.length) {
            for(var i = 0; i < item.AudioStreamCodecs.length - 1; i++) {
               

                html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 1px 1px 1px;border-radius: 5px; margin:2px; font-size: 11px; text-align:center">' + item.AudioStreamCodecs[i] + '</span>';
            }
            
        }
        html += '</td>';
        

        ////Internal Subtitles
        //html += '<td class="detailTableBodyCell fileCell" data-title="Subtitles">';
        //if (item.Subtitles && item.Subtitles.length) {
        //    html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 10px 1px 10px;border-radius: 5px;margin:2px;font-size: 11px; text-align:center">' + item.Subtitles[0].toLocaleUpperCase() + '</span>';
        //}
        
        html += '</td>';
        

        //Action
        html += '<td class="detailTableBodyCell fileCell" data-title="Action">';
        var processBtn = getButtonSvgIconRenderData("ProcessBtn");
        html += '<button type="button" data-resultid="' + item.Id + '" class="btnProcessResult autoSize emby-button" title="Organize" style="background-color:transparent">';
        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
        html += '<path fill="var(--focus-background)" d="' + processBtn.path + '"/>';
        html += '</svg>';
        html += '</button>';

        var deleteBtn = getButtonSvgIconRenderData("DeleteBtn");
        html += '<button type="button" data-resultid="' + item.Id + '" class="btnDeleteResult autoSize emby-button" title="Delete" style="background-color:transparent">';
        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
        html += '<path fill="var(--focus-background)" d="' + deleteBtn.path + '"/>';
        html += '</svg>';
        html += '</button>';
        html += '</td>';

        html += '</tr>';


        //Library Item
        html += '<tr class="detailTableBodyRow detailTableBodyRow-shaded">';
        
        html += '<td style="border-spacing:0;padding:.4em">';
        html += 'Library'
        html += '</td>';   
        
        //File Size
        html += '<td style="border-spacing:0;padding:.4em" data-title="File Size">';
        html += '<span>' + formatBytes(libraryItem.MediaSources[0].Size) + '</span>';
        html += '</td>';

        //Release Edition
        html += '<td style="border-spacing:0;padding:.4em" data-title="Edition">';
        switch(item.Type) {
        case "Episode":
            
            html += '<span>' + libraryItem.ParentIndexNumber + 'x' + (libraryItem.IndexNumber <= 9 ? `0${libraryItem.IndexNumber}` : libraryItem.IndexNumber) + '</span>';
            
        case "Movie":
            html += '<span>' + "" + '</span>';  
        }
        html += '</td>';

        //Quality
        html += '<td style="border-spacing:0;padding:.4em" data-title="Resolution">';
        html += '<span style="color: white;background-color: rgb(131,131,131); padding: 5px 10px 5px 10px;border-radius: 5px;font-size: 11px;">' + libraryItem.MediaSources[0].MediaStreams.filter(s => s.Type === "Video")[0].DisplayTitle.split(' ')[0]  + '</span>';  
        html += '</td>';

        //Quality is up or down
        html += '<td style="border-spacing:0;padding:.4em" data-title="Resolution">';
        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';

        if (libraryItemResolution == sourceResolution) {
            html += ' <path fill="green" d="M19,10H5V8H19V10M19,16H5V14H19V16Z" /> ';
        }
        else if (libraryItemResolution > sourceResolution) {
            html += '<path fill="green" d="M7.03 9.97H11.03V18.89L13.04 18.92V9.97H17.03L12.03 4.97Z" />';
        } 
        else {
            html += ' <path fill="red" d="M7.03 13.92H11.03V5L13.04 4.97V13.92H17.03L12.03 18.92Z" />';
        }
        html += '</svg>';  
        html += '</td>';

        //Codec
        html += '<td style="border-spacing:0;padding:.4em" data-title="Codec">';
        html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 10px 1px 10px;border-radius: 5px;margin:2px;font-size: 11px; text-align:center">' + libraryItem.MediaSources[0].MediaStreams.filter(s => s.Type === "Video")[0].Codec.toLocaleUpperCase() + '</span>'; 
        html += '</td>';

        //Audio
        html += '<td style="border-spacing:0;padding:.4em" data-title="Audio">';
        html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 1px 1px 1px;border-radius: 5px; margin:2px; font-size: 11px; text-align:center">' + libraryItem.MediaSources[0].MediaStreams.filter(s => s.Type === "Audio")[0].Codec.toLocaleUpperCase() + '</span>';
        html += '</td>';

        ////Internal Subtitle
        //html += '<td class="detailTableBodyCell fileCell" data-title="Subtitle">';
        //var subtitles = libraryItem.MediaSources[0].MediaStreams.filter(s => s.Type === "Subtitle")[0]
        //var subtitleLabel = subtitles && !subtitles.IsExternal ? subtitles.DisplayLanguage.toLocaleUpperCase() : "";
        //html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 10px 1px 10px;border-radius: 5px;margin:2px;font-size: 11px; text-align:center">' + subtitleLabel + '</span>';
        //html += '</td>';

        //Action
        html += '<td style="border-spacing:0;padding:.4em" data-title="Resolution">';
        
        html += '</td>';

        html += '</tr>';
        html += '</tbody>'
        html += '</table>';

        html += '</div>';

        dlg.innerHTML = html;

        dlg.querySelector('.btnCloseDialog').addEventListener('click',
            () => {
                dialogHelper.close(dlg);
            });

        dlg.querySelectorAll('.btnProcessResult').forEach(btn => {
            btn.addEventListener('click',
                async (e) => {
                    //let id = e.target.closest('button').getAttribute('data-resultid');
                    organizeFile(e.view, id);
                    await reloadItems(view, false)
                    dialogHelper.close(dlg);
                })
        });

        dlg.querySelectorAll('.btnDeleteResult').forEach(btn => {
            btn.addEventListener('click',
                async (e) => {
                    //let id = e.target.closest('button').getAttribute('data-resultid');
                    deleteOriginalFile(e.view, id);
                    await reloadItems(view, false)
                    dialogHelper.close(dlg);
                });
        })

        dialogHelper.open(dlg);
    }

    async function reloadItems(page, showSpinner, searchTerm = "") {

        
        if (showSpinner) {
            loading.show();
        }

        //Search Term from Search Box.
        query.NameStartsWith = encodeURI(searchTerm);
        

        var result = await ApiClient.getFileOrganizationResults(query)

        currentResult = result;

        await renderResults(page, result);

        page.querySelectorAll('.btnShowStatusMessage').forEach(btn => {
            btn.addEventListener('click',
                (e) => {
                    let id = e.target.getAttribute('data-resultid');
                    showStatusMessage(id);
                });
        })

        page.querySelectorAll('.btnIdentifyResult').forEach(btn => {
            btn.addEventListener('click',
                (e) => {
                    e.preventDefault();
                    let id = e.target.closest('button').getAttribute('data-resultid');
                    var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];
                    showCorrectionPopup(e.view, item);
                });
        })

        page.querySelectorAll('.btnProcessResult').forEach(btn => {
            btn.addEventListener('click',
                (e) => {
                    let id = e.target.closest('button').getAttribute('data-resultid');
                    organizeFile(e.view, id);
                })
        });

        page.querySelectorAll('.btnDeleteResult').forEach(btn => {
            btn.addEventListener('click',
                async (e) => {
                    let id = e.target.closest('button').getAttribute('data-resultid');
                    deleteOriginalFile(e.view, id);
                    result = await ApiClient.getFileOrganizationResults(query)
                    currentResult = result;
                    await renderResults(page, result);
                });
        })

        page.querySelectorAll('.btnShowSubtitleList').forEach(btn => btn.addEventListener('click',
            (e) => {
                let id = e.target.getAttribute('data-resultid');
                var subtitles = currentResult.Items.filter(function (i) { return i.Id === id; })[0].Subtitles;
                var msg = "";
                subtitles.forEach(t => {
                    msg += t + '\n';
                })
                Dashboard.alert({
                    title: "Subtitles",
                    message: msg
                });
            }));

        var statusIcons = [...page.querySelectorAll('.statusIcon')];
        var itemsToCompare = statusIcons.filter(icon => icon.dataset.status === "SkippedExisting" ||
            icon.dataset.status === "NewEdition" ||
            icon.dataset.status === "NewResolution");

        itemsToCompare.forEach(i => {
            i.style.cursor = "pointer";
            i.addEventListener('click',
                async (e) => {
                    var id = e.target.closest('svg').dataset.resultid;
                    await openComparisonDialog(id);
                })

        })
        
        loading.hide();
    }

    
    function getQueryPagingHtml(options) {
        var startIndex = options.startIndex;
        var limit = options.limit;
        var totalRecordCount = options.totalRecordCount;

        var html = '';

        var recordsEnd = Math.min(startIndex + limit, totalRecordCount);

        var showControls = limit < totalRecordCount;

        html += '<div class="listPaging">';

        if (showControls) {
            html += '<span style="vertical-align:middle;">';

            var startAtDisplay = totalRecordCount ? startIndex + 1 : 0;
            html += startAtDisplay + '-' + recordsEnd + ' of ' + totalRecordCount;

            html += '</span>';

            html += '<div style="display:inline-block;">';

            html += '<button is="paper-icon-button-light" class="btnPreviousPage autoSize" ' + (startIndex ? '' : 'disabled') + '><i class="md-icon">&#xE5C4;</i></button>';
            html += '<button is="paper-icon-button-light" class="btnNextPage autoSize" ' + (startIndex + limit >= totalRecordCount ? 'disabled' : '') + '><i class="md-icon">&#xE5C8;</i></button>';

            html += '</div>';
        }

        html += '</div>';

        return html;
    }

   
    function renderResults(page, result) {

        if (Object.prototype.toString.call(page) !== "[object Window]") {

            var items = result.Items;

            var table = page.querySelector('.autoorganizetable');
            var mobileCardsContainer = page.querySelector('.mobileOrganizeMobileCardsContainer');
            var mobileCards = page.querySelector('.autoOrganizeMobileCards');
            var organizeTaskPanel = page.querySelector('.organizeTaskPanel');
            if (document.body.clientWidth > 1200) {

                //We are rendering fullscreen table results
                organizeTaskPanel.style.width = "80%";

                table.classList.remove('hide');
                mobileCardsContainer.classList.add('hide');

                var resultBody = page.querySelector('.resultBody');

                resultBody.innerHTML = '';

                items.forEach(async item => {

                    var html = '';

                    html += '<tr class="detailTableBodyRow detailTableBodyRow-shaded" id="row' + item.Id + '" style="color: var(--theme-primary-text);">';

                    html += await renderTableRow(item);

                    html += '</tr>';

                    resultBody.innerHTML += html;

                })

                resultBody.addEventListener('click', handleItemClick);

                
            } else {

                organizeTaskPanel.style.width = "90%";
                //We are rendering mobile result cards
                table.classList.add('hide');
                mobileCardsContainer.classList.remove('hide');
                var html = '';
                items.forEach(item => {
                    
                    html += renderItemCard(item);
                })

                mobileCards.innerHTML = html;
            }


            //page.querySelectorAll('.btnShowStatusMessage').forEach(btn => {
            //    btn.addEventListener('click',
            //        (e) => {
            //            let id = e.target.getAttribute('data-resultid');
            //            showStatusMessage(id);
            //        });
            //})

            //page.querySelectorAll('.btnIdentifyResult').forEach(btn => {
            //    btn.addEventListener('click',
            //        (e) => {
            //            e.preventDefault();
            //            let id = e.target.closest('button').getAttribute('data-resultid');
            //            var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];
            //            showCorrectionPopup(e.view, item);
            //        });
            //})

            //page.querySelectorAll('.btnProcessResult').forEach(btn => {
            //    btn.addEventListener('click',
            //        (e) => {
            //            let id = e.target.closest('button').getAttribute('data-resultid');
            //            organizeFile(e.view, id);
            //        })
            //});

            //page.querySelectorAll('.btnDeleteResult').forEach(btn => {
            //    btn.addEventListener('click',
            //        (e) => {
            //            let id = e.target.closest('button').getAttribute('data-resultid');
            //            deleteOriginalFile(e.view, id);
            //        });
            //})

            //var statusIcons = [...page.querySelectorAll('.statusIcon')];
            //var itemsToCompare = statusIcons.filter(icon => icon.dataset.status === "SkippedExisting" ||
            //    icon.dataset.status === "NewEdition" ||
            //    icon.dataset.status === "NewResolution");

            //itemsToCompare.forEach(i => {
            //    i.style.cursor = "pointer";
            //    i.addEventListener('click',
            //        async (e) => {
            //            var id = e.target.closest('svg').dataset.resultid;
            //            await openComparisonDialog(id);
            //        })

            //})

            //page.querySelectorAll('.btnShowSubtitleList').forEach(btn => btn.addEventListener('click',
            //    (e) => {
            //        let id = e.target.getAttribute('data-resultid');
            //        var subtitles = currentResult.Items.filter(function (i) { return i.Id === id; })[0].Subtitles;
            //        var msg = "";
            //        subtitles.forEach(t => {
            //            msg += t + '\n';
            //        })
            //        Dashboard.alert({
            //            title: "Subtitles",
            //            message: msg
            //        });
            //    }));


            var pagingHtml = getQueryPagingHtml({
                startIndex: query.StartIndex,
                limit: query.Limit,
                totalRecordCount: result.TotalRecordCount,
                showLimit: false,
                updatePageSizeSetting: false
            });

            var topPaging = page.querySelector('.listTopPaging');
            topPaging.innerHTML = pagingHtml;

            var bottomPaging = page.querySelector('.listBottomPaging');
            bottomPaging.innerHTML = pagingHtml;

            var btnNextTop = topPaging.querySelector(".btnNextPage");
            var btnNextBottom = bottomPaging.querySelector(".btnNextPage");
            var btnPrevTop = topPaging.querySelector(".btnPreviousPage");
            var btnPrevBottom = bottomPaging.querySelector(".btnPreviousPage");

            if (btnNextTop) {
                btnNextTop.addEventListener('click', async function () {
                    query.StartIndex += query.Limit;
                    await reloadItems(page, true);
                });
            }

            if (btnNextBottom) {
                btnNextBottom.addEventListener('click', async function () {
                    query.StartIndex += query.Limit;
                    await reloadItems(page, true);
                });
            }

            if (btnPrevTop) {
                btnPrevTop.addEventListener('click', async function () {
                    query.StartIndex -= query.Limit;
                    await reloadItems(page, true);
                });
            }

            if (btnPrevBottom) {
                btnPrevBottom.addEventListener('click', async function () {
                    query.StartIndex -= query.Limit;
                    await reloadItems(page, true);
                });
            }

        }

    }

   

    function getResultItemTypeIcon(type) {
        switch (type) {
            case "Unknown": return { path: "" }
            case "Movie": return { path: "M14.75 5.46L12 1.93L13.97 1.54L16.71 5.07L14.75 5.46M21.62 4.1L20.84 .18L16.91 .96L19.65 4.5L21.62 4.1M11.81 6.05L9.07 2.5L7.1 2.91L9.85 6.44L11.81 6.05M2 8V18C2 19.11 2.9 20 4 20H20C21.11 20 22 19.11 22 18V8H2M4.16 3.5L3.18 3.69C2.1 3.91 1.4 4.96 1.61 6.04L2 8L6.9 7.03L4.16 3.5M11 24H13V22H11V24M7 24H9V22H7V24M15 24H17V22H15V24Z" }
            case "Episode": return { path: "M8.16,3L6.75,4.41L9.34,7H4C2.89,7 2,7.89 2,9V19C2,20.11 2.89,21 4,21H20C21.11,21 22,20.11 22,19V9C22,7.89 21.11,7 20,7H14.66L17.25,4.41L15.84,3L12,6.84L8.16,3M4,9H17V19H4V9M19.5,9A1,1 0 0,1 20.5,10A1,1 0 0,1 19.5,11A1,1 0 0,1 18.5,10A1,1 0 0,1 19.5,9M19.5,12A1,1 0 0,1 20.5,13A1,1 0 0,1 19.5,14A1,1 0 0,1 18.5,13A1,1 0 0,1 19.5,12Z" }
            case "Song": return { path: "M21,3V15.5A3.5,3.5 0 0,1 17.5,19A3.5,3.5 0 0,1 14,15.5A3.5,3.5 0 0,1 17.5,12C18.04,12 18.55,12.12 19,12.34V6.47L9,8.6V17.5A3.5,3.5 0 0,1 5.5,21A3.5,3.5 0 0,1 2,17.5A3.5,3.5 0 0,1 5.5,14C6.04,14 6.55,14.12 7,14.34V6L21,3Z" }
        }
    }

    function getButtonSvgIconRenderData(btn_icon) {
        switch (btn_icon) {
            case 'IdentifyBtn': return {
                path: "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H13C12.59,21.75 12.2,21.44 11.86,21.1C11.53,20.77 11.25,20.4 11,20H6V4H13V9H18V10.18C18.71,10.34 19.39,10.61 20,11V8L14,2M20.31,18.9C21.64,16.79 21,14 18.91,12.68C16.8,11.35 14,12 12.69,14.08C11.35,16.19 12,18.97 14.09,20.3C15.55,21.23 17.41,21.23 18.88,20.32L22,23.39L23.39,22L20.31,18.9M16.5,19A2.5,2.5 0 0,1 14,16.5A2.5,2.5 0 0,1 16.5,14A2.5,2.5 0 0,1 19,16.5A2.5,2.5 0 0,1 16.5,19Z",
                color: 'var(--focus-background)'
            }
            case 'DeleteBtn': return {
                path: "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z",
                color: 'var(--focus-background) '
            };
            case 'ProcessBtn': return {
                path: "M4 7C4 4.79 7.58 3 12 3S20 4.79 20 7 16.42 11 12 11 4 9.21 4 7M19.72 13.05C19.9 12.71 20 12.36 20 12V9C20 11.21 16.42 13 12 13S4 11.21 4 9V12C4 14.21 7.58 16 12 16C12.65 16 13.28 15.96 13.88 15.89C14.93 14.16 16.83 13 19 13C19.24 13 19.5 13 19.72 13.05M13.1 17.96C12.74 18 12.37 18 12 18C7.58 18 4 16.21 4 14V17C4 19.21 7.58 21 12 21C12.46 21 12.9 21 13.33 20.94C13.12 20.33 13 19.68 13 19C13 18.64 13.04 18.3 13.1 17.96M23 19L20 16V18H16V20H20V22L23 19Z",
                color: 'var(--focus-background)'
            }
        }
    }

    function getStatusRenderData(status) {
        switch (status) {
            case 'Success': return {
                path: "M10,17L5,12L6.41,10.58L10,14.17L17.59,6.58L19,8M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                color: "green",
                text: "Complete",
                active: false
            };
            case 'Failure': return {
                path: "M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z",
                color: "orangered",
                text: "Attention - Unidentified" ,
                active:false
            };
            case 'SkippedExisting': return {
                path: "M13 14H11V9H13M13 18H11V16H13M1 21H23L12 2L1 21Z",
                color: "goldenrod",
                text: "Existing Item",
                active:false
            };
            case 'Processing': return {
                path: "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 16.2L14 17L11 11.8V7H12.5V11.4L15.3 16.2Z",
                color: "var(--theme-accent-text-color)",
                text: "Processing..." ,
                active:true
            };
            case "Checking": return {
                path: "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 16.2L14 17L11 11.8V7H12.5V11.4L15.3 16.2Z",
                color: "goldenrod",
                text: "Checking...",
                active:false
            };
            case "NewResolution": return {
                path: "M12 5.5L10 8H14L12 5.5M18 10V14L20.5 12L18 10M6 10L3.5 12L6 14V10M14 16H10L12 18.5L14 16M21 3H3C1.9 3 1 3.9 1 5V19C1 20.1 1.9 21 3 21H21C22.1 21 23 20.1 23 19V5C23 3.9 22.1 3 21 3M21 19H3V5H21V19Z",
                color: "var(--theme-accent-text-color)",
                text: "New Resolution" ,
                active:false
            }
            case "NotEnoughDiskSpace": return {
                path: "",
                color: "orangered",
                text: "Not Enough Disk Space!",
                active:false
            }
            case "InUse": return {
                path: "M22 12C22 6.46 17.54 2 12 2C10.83 2 9.7 2.19 8.62 2.56L9.32 4.5C10.17 4.16 11.06 3.97 12 3.97C16.41 3.97 20.03 7.59 20.03 12C20.03 16.41 16.41 20.03 12 20.03C7.59 20.03 3.97 16.41 3.97 12C3.97 11.06 4.16 10.12 4.5 9.28L2.56 8.62C2.19 9.7 2 10.83 2 12C2 17.54 6.46 22 12 22C17.54 22 22 17.54 22 12M5.47 7C4.68 7 3.97 6.32 3.97 5.47C3.97 4.68 4.68 3.97 5.47 3.97C6.32 3.97 7 4.68 7 5.47C7 6.32 6.32 7 5.47 7M9 9H11V15H9M13 9H15V15H13",
                color: "goldenrod",
                text: "File in use" ,
                active:false
            }
            case 'UserInputRequired': return {
                path: "M21.7,13.35L20.7,14.35L18.65,12.3L19.65,11.3C19.86,11.09 20.21,11.09 20.42,11.3L21.7,12.58C21.91,12.79 21.91,13.14 21.7,13.35M12,18.94L18.06,12.88L20.11,14.93L14.06,21H12V18.94M12,14C7.58,14 4,15.79 4,18V20H10V18.11L14,14.11C13.34,14.03 12.67,14 12,14M12,4A4,4 0 0,0 8,8A4,4 0 0,0 12,12A4,4 0 0,0 16,8A4,4 0 0,0 12,4Z",
                color: "goldenrod",
                text: "Pending..." ,
                active:false
            }
            case "NewMedia": return {
                path: "M20,4C21.11,4 22,4.89 22,6V18C22,19.11 21.11,20 20,20H4C2.89,20 2,19.11 2,18V6C2,4.89 2.89,4 4,4H20M8.5,15V9H7.25V12.5L4.75,9H3.5V15H4.75V11.5L7.3,15H8.5M13.5,10.26V9H9.5V15H13.5V13.75H11V12.64H13.5V11.38H11V10.26H13.5M20.5,14V9H19.25V13.5H18.13V10H16.88V13.5H15.75V9H14.5V14A1,1 0 0,0 15.5,15H19.5A1,1 0 0,0 20.5,14Z" ,
                color: "green",
                text: "New Media" ,
                active:false
            }
            case "NewEdition": return {
                path : "M19.65 6.5L16.91 2.96L20.84 2.18L21.62 6.1L19.65 6.5M16.71 7.07L13.97 3.54L12 3.93L14.75 7.46L16.71 7.07M19 13C20.1 13 21.12 13.3 22 13.81V10H2V20C2 21.11 2.9 22 4 22H13.81C13.3 21.12 13 20.1 13 19C13 15.69 15.69 13 19 13M11.81 8.05L9.07 4.5L7.1 4.91L9.85 8.44L11.81 8.05M4.16 5.5L3.18 5.69C2.1 5.91 1.4 6.96 1.61 8.04L2 10L6.9 9.03L4.16 5.5M20 18V15H18V18H15V20H18V23H20V20H23V18H20Z",
                color: "var(--theme-accent-text-color)",
                text: "New Edition",
                active:false
            };
        }
    }

    

    function showStatusMessage(id) {
        var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];
        var renderStatusData = getStatusRenderData(item.Status);
        var msg = item.StatusMessage || '';

        Dashboard.alert({
            title: renderStatusData.text,
            message: msg
        });
    }
    
    function animateElement(ele, animation) {
        switch (animation) {
        case "rotate":
            ele.animate([
                // keyframes
                { transform: 'rotate(360deg)' }
            ], {
                // timing options
                duration: 1000,
                iterations: Infinity
            })
            break;
        case "blink":
            ele.animate([
                { opacity: 0},
                {opacity:1},
                {opacity:0}
            ], {duration : 500, iterations: Infinity})
        }
        
        
    }
    
    function renderItemCard(item) {

        var html = '';

        //TODO: Don't repeat this code for both table and card renders
        var statusRenderData = item.IsInProgress && item.Status !== "Processing" && item.Status !== "Failure" //We're in some kind of progress, but not processing, or failing. We must be 'checking'
            ? getStatusRenderData("Checking") 
            : item.IsInProgress && item.Status === "Failure" //We failed before, but now we are processing. 
            ? getStatusRenderData("Processing") 
            : getStatusRenderData(item.Status); //The actual status icon

        html += '<div data-resultid="' + item.Id + '" class="card" style="margin: 1em;padding: 1em;border: 1px darkgrey solid;border-radius: 6px; box-shadow:-1px 2px 1px 0px rgb(0 0 0 / 10%)">';

        //Row 1 - Top
        html += '<div style="display:flex; align-items:center">';
        //Status Icon
        html += '<div class="progressIcon">';
        html += '<svg class="statusIcon" style="width:24px; height:24px;padding-top: 6px;padding-right: 6px;" viewBox="0 0 24 24" data-resultid="' + item.Id + '" data-status="' + item.Status + '" data-active="' + statusRenderData.active + '" data-step="1">';
        html += '<path fill="' + statusRenderData.color + '" d="' + statusRenderData.path + '"/>';
        html += '</svg>';
        html += '</div>';
        //Status Text
        html += '<span style="padding-right:1em">' + statusRenderData.text + '</span>';

        //Date
        var date = datetime.parseISO8601Date(item.Date, true);
        html += '<span style="padding-right:1em">' + datetime.toLocaleDateString(date)  + '</span>';

        html += '<div style="flex-grow:1"></div>'; //Spacing for the quality badge

        //Quality
        html += '<span style="color: white;background-color: var(--theme-accent-text-color); padding: .5em 1em 0.5em 1em;border-radius: 5px;font-size: 0.7em;">' + (item.SourceQuality ? item.SourceQuality.toLocaleUpperCase() : "") + " " + (item.ExtractedResolution.Name ?? "")  + '</span>';
        html += '</div>';

        //Row 2- Middle
        html += '<div style="display:flex; align-items:center">';

       //Type icon
       var icon = getResultItemTypeIcon(item.Type);
       html += '<div class="type-icon-container" style="padding-right:1em">';
       html += '<svg id="typeIcon" style="width:24px;height:24px" viewBox="0 0 24 24">';
       html += '<path fill="' + statusRenderData.color + '" d="' + icon.path + '"/>';
       html += '</svg>';
       html += '</div>';

        //Name
        html += '<span id="string_name_' + item.Id + '" style="padding-right:1em">' + formatItemName(item.ExtractedName ?? "") + '</span>';
        //Release/Edition
        switch(item.Type) {
        case "Episode":
            if (item.ExtractedSeasonNumber && item.ExtractedEpisodeNumber) {
                html += '<span style="padding-right:1em">' + item.ExtractedSeasonNumber + 'x' + (item.ExtractedEpisodeNumber <= 9 ? `0${item.ExtractedEpisodeNumber}` : item.ExtractedEpisodeNumber) + '</span>';
            } else {
                html += '';
            }
        case "Movie":
            html += '<span style="padding-right:1em">' + item.ExtractedEdition ?? "" + '</span>';  
        }
        html += '</div>';

        //Row 3 - Bottom
        html += '<div style="display:flex; align-items:center" class="actionButtonContainer">'
        html += renderActionButtons(item);
        html += '</div>';

        html += '</div>';

        return html;
    }

    async function renderTableRow(item) {
        
        var html = '';
        //TODO: Don't repeat this code for both table and card renders
        var statusRenderData = item.IsInProgress && item.Status !== "Processing" && item.Status !== "Failure" //We're in some kind of progress, but not processing, or failing. We must be 'checking'
            ? getStatusRenderData("Checking") 
            : item.IsInProgress && item.Status === "Failure" //We failed before, but now we are processing. 
            ? getStatusRenderData("Processing") 
            : getStatusRenderData(item.Status); //The actual status icon
        
        //Status Icon
        html += '<td class="detailTableBodyCell">';
        html += '<div class="progressIcon">';
        html += '<svg class="statusIcon" style="width:24px; height:24px;" viewBox="0 0 24 24" data-resultid="' + item.Id + '" data-status="' + item.Status + '" data-active="' + statusRenderData.active + '" data-step="1">';
        html += '<path fill="' + statusRenderData.color + '" d="' + statusRenderData.path + '"/>';
        html += '</svg>';
        html += '</div>';
        html += '</td>';

        //Date
        html += '<td class="detailTableBodyCell" data-title="Date">';
        var date = datetime.parseISO8601Date(item.Date, true);
        html += '<span>' + datetime.toLocaleDateString(date)  + '</span>';
        html += '</td>';

        //Status
        html += '<td data-resultid="' + item.Id + '" class= class="fileCell" style="white-space:normal; border-spacing:0;padding:.4em">';
        html += '<span>' + statusRenderData.text + '</span>';
        html += '</td>';

        //Name
        html += '<td data-resultid="' + item.Id + '" class="fileCell cellName_' + item.Id + '" style="border-spacing:0;padding:.4em">';
               
        html += '<span id="string_name_' + item.Id + '">' + formatItemName(item.ExtractedName ?? "") + '</span>';

        html += '</td>';             

        //Release Edition
        html += '<td class="fileCell" data-title="Edition" style="border-spacing:0;padding:.4em">';
        switch(item.Type) {
            case "Episode":
                if (item.ExtractedSeasonNumber && item.ExtractedEpisodeNumber) {
                    html += '<span>';
                    html += item.ExtractedSeasonNumber + 'x' + (item.ExtractedEpisodeNumber <= 9 ? `0${item.ExtractedEpisodeNumber}` : item.ExtractedEpisodeNumber);
                    if (item.ExtractedEndingEpisodeNumber) {
                        html += '-' + (item.ExtractedEndingEpisodeNumber <= 9 ? `0${item.ExtractedEndingEpisodeNumber}` : item.ExtractedEndingEpisodeNumber)
                    }
                    html += '</span>';
                } else {
                    html += '';
                }
            case "Movie":
                html += '<span>' + item.ExtractedEdition ?? "" + '</span>';  
        }
       
        html += '</td>';

        //Quality
        html += '<td class="fileCell" data-title="Resolution"style="border-spacing:0;padding:.4em width:5em">';
        html += '<span style="color: white;background-color: var(--theme-accent-text-color); padding: .5em 1em 0.5em 1em;border-radius: 5px;font-size: 0.7em;">' + (item.SourceQuality ? item.SourceQuality.toLocaleUpperCase() : "") + " " + (item.ExtractedResolution.Name ?? "")  + '</span>';  
        html += '</td>';

        //Codec
        html += '<td class="fileCell" data-title="Codec" style="border-spacing:0;padding:.4em width:8em">';
        if (item.VideoStreamCodecs.length) {
            html += '<div style="display:flex; flex-direction:row">';
            html += '<div style="display:flex; flex-direction:column">'
            for(var i = 0; i <= item.VideoStreamCodecs.length-1; i++) {
                if (i > 1 && i % 2 === 0) {
                    html += '</div>';
                    html += '<div style="display:flex; flex-direction:column">'
                }
                html += '<span style="color: white;background-color: rgb(131,131,131); padding: .5em 1em 0.5em 1em;border-radius: 5px;margin:2px;font-size: 0.7em; text-align:center">' + item.VideoStreamCodecs[i] + '</span>'; 
            }
            html += '</div>';
            html += '</div>';
        }
        
        html += '</td>';

        //Audio
        html += '<td class="fileCell" data-title="Audio" style="border-spacing:0;padding:.4em">';
        if (item.AudioStreamCodecs.length) {
           
            html += '<div style="display:flex; flex-direction:column">'
            html += '<span style="color: white;background-color: rgb(131,131,131); padding: 0.5em 0.5em 0.5em 0.5em;border-radius: 5px; margin:2px; font-size: 0.7em; text-align:center">' + item.AudioStreamCodecs[0].toLocaleUpperCase() + '</span>';
            //html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 10px 1px 10px;border-radius: 5px; margin:2px; font-size: 11px;">' + item.AudioStreamCodecs[1].toLocaleUpperCase() + '</span>';
            html += '</div>';
            
        }

        html += '</td>';

        //Internal Subtitles
        html += '<td class="fileCell" data-title="Subtitles" style="border-spacing:0;padding:.4em">';
        if (item.Subtitles.length) {

            html += '<svg style="width:24px;height:24px; cursor:pointer;" viewBox="0 0 24 24" data-resultid="' + item.Id + '" class="btnShowSubtitleList">';
            html += '<path fill="var(--theme-accent-text-color)" d="M18,11H16.5V10.5H14.5V13.5H16.5V13H18V14A1,1 0 0,1 17,15H14A1,1 0 0,1 13,14V10A1,1 0 0,1 14,9H17A1,1 0 0,1 18,10M11,11H9.5V10.5H7.5V13.5H9.5V13H11V14A1,1 0 0,1 10,15H7A1,1 0 0,1 6,14V10A1,1 0 0,1 7,9H10A1,1 0 0,1 11,10M19,4H5C3.89,4 3,4.89 3,6V18A2,2 0 0,0 5,20H19A2,2 0 0,0 21,18V6C21,4.89 20.1,4 19,4Z"  />';
            html += '</svg>';

            

            //html += '<div style="display:flex; flex-direction:row">';
            //html += '<div style="display:flex; flex-direction:column">'
            //for(var i = 0; i <= item.Subtitles.length-1; i++) {
            //    if (i > 1 && i % 2 === 0) {
            //        html += '</div>';
            //        html += '<div style="display:flex; flex-direction:column">'
            //    }
            //    html += '<span style="color: white;background-color: rgb(131,131,131); padding: 1px 10px 1px 10px;border-radius: 5px;margin:2px;font-size: 11px; text-align:center">' + item.Subtitles[i].toLocaleUpperCase() + '</span>'; 
            //}
            //html += '</div>';
            //html += '</div>';
        }
        
        html += '</td>';

        //File Size
        html += '<td class="fileCell" data-title="File Size" style="border-spacing:0;padding:.4em">';
        html += '<span>' + formatBytes(item.FileSize) + '</span>';
        html += '</td>';        

        //Media Type Icon (Movie/Episode)
        var icon = getResultItemTypeIcon(item.Type)
        html += '<td style="border-spacing:0;padding:.4em">';
        html += '<div class="type-icon-container">';
        html += '<svg id="typeIcon" style="width:24px;height:24px" viewBox="0 0 24 24">';
        html += '<path fill="' + statusRenderData.color + '" d="' + icon.path + '"/>';
        html += '</svg>';
        html += '</div>';
        html += '</td>';

        //Source file path
        html += '<td data-title="Source" class="fileCell" style="border-spacing:0;padding:.4em white-space: normal">';
        html += '<a is="emby-linkbutton" data-resultid="' + item.Id + '" style="color:' + statusRenderData.color + ';white-space: normal;" href="#" class="button-link btnShowStatusMessage">';
        html += item.OriginalFileName.replaceAll('.', '<wbr>.'); //<== Add a word break opportunity to file names because they may be really long, and cause the table to go off screen.
        html += '</a>';
        html += '</td>';

        //Destination file path
        html += '<td data-title="Destination" data-type="' + item.Type + '" data-name="' + item.ExtractedName + '" data-season="' + (item.ExtractedSeasonNumber ?? '') + '" data-episode="' + (item.ExtractedEpisodeNumber ?? '') + '" class="fileCell" style="border-spacing:0;padding:.4em white-space: normal">';
        html += item.TargetPath || '';
        html += '</td>';                                 

        //Row sorting options (action buttons)
        html += '<td data-title="Actions" style="border-spacing:0;padding:.4em white-space:normal;">';
        html += renderActionButtons(item);
        html += '</td>';

        html += '<td class="organizerButtonCell" style="border-spacing:0;padding:.4em white-space:no-wrap;"></td>';
        
        return html;
    }

    function renderActionButtons(item) {
        var html = '';
        if (item.Status === "Checking" || item.Status === "InUse") {
            return html;
        } else {
            if (item.Status !== 'Success') {
                if (item.Status !== "Processing") {
                    //Identify Entry Button - This opens the Identify/Lookup modal for the item.
                    //We want to show this option if the item has been skipped because we think it already exists, or the item failed to find a match.
                    //There is a chance that the Lookup was incorrect if there was a match to an existing item.
                    //Allow the user to identify the item.
                    if (item.Status === "Failure" || (item.Status === "UserInputRequired")) { //&& !item.TargetPath)) {
                        var identifyBtn = getButtonSvgIconRenderData("IdentifyBtn");
                        html += '<button type="button" data-resultid="' + item.Id + '" data-type="' + item.Type + '" class="btnIdentifyResult organizerButton autoSize emby-button" title="Identify" style="background-color:transparent">';
                        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
                        html += '<path fill="var(--focus-background)" d="' + identifyBtn.path + '"/>';
                        html += '</svg>';
                        html += '</button>';
                    }

                    //Process Entry Button - This will process the item into the library based on the info in the "Destination" column of the table row.
                    //Only show this button option if: it is not a Success, not Processing, and has not failed to find a possible result.
                    //The "Destination" column info will be populated.
                    if (item.Status === "NewResolution" && item.TargetPath ||
                        item.Status === "SkippedExisting" && item.TargetPath ||
                        //item.Status === "Waiting" && item.TargetPath || 
                        item.Status === "NewMedia" && item.TargetPath ||
                        item.Status === "NewEdition") {

                        var processBtn = getButtonSvgIconRenderData("ProcessBtn");
                        html += '<button type="button" data-resultid="' + item.Id + '" class="btnProcessResult organizerButton autoSize emby-button" title="Organize" style="background-color:transparent">';
                        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
                        html += '<path fill="var(--focus-background)" d="' + processBtn.path + '"/>';
                        html += '</svg>';
                        html += '</button>';
                    }
                }

                //Delete Entry Button - This deletes the item from the log window and removes it from watched folder - always show this option
                var deleteBtn = getButtonSvgIconRenderData("DeleteBtn");
                html += '<button type="button" data-resultid="' + item.Id + '" class="btnDeleteResult organizerButton autoSize emby-button" title="Delete" style="background-color:transparent">';
                html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
                html += '<path fill="var(--focus-background)" d="' + deleteBtn.path + '"/>';
                html += '</svg>';
                html += '</button>';
                //html += '</td>';
            }

            return html;
        }
    }

    function handleItemClick(e) {

        var id;

        var buttonRemoveSmartMatchResult = parentWithClass(e.target, 'btnRemoveSmartMatchResult');
        if (buttonRemoveSmartMatchResult) {

            id = buttonRemoveSmartMatchResult.getAttribute('data-resultid');

            var smartMatchEntryList = getSmartMatchInfos();
            var smartMatchListItemValue = "";

            smartMatchEntryList.forEach(item => {
                if (item.Name == id) {
                    smartMatchListItemValue == item.Value;
                }
            })

            var entries = [
                {
                    Name: id,
                    Value: smartMatchListItemValue
                }];
            deleteSmartMatchEntries(entries)
        }
    }

    async function onServerEvent(e, apiClient, data) {
        if (e.type === "TaskComplete") {
            if (data && data == 'AutoOrganize') {
                await reloadItems(pageGlobal, false);
                pageGlobal.querySelector('.organizeProgress').classList.add('hide');
            }
        }
        if (e.type === 'ScheduledTasksInfoStop') {

            if (data && data.ScheduledTask.Key === 'AutoOrganize') {
                await reloadItems(pageGlobal, false);
            }

        } else if (e.type === 'TaskData') {

            if (data && data.ScheduledTask.Key === 'AutoOrganize') {
                
                updateTaskScheduleLastRun(data);
                //var taskProgress = pageGlobal.querySelector('.organizeProgress');
                //taskProgress.classList.remove('hide');
                //animateElement(taskProgress.querySelector('svg'), "blink");
                
                //checkUpdatingTableItems(pageGlobal);
            }

        } else if (e.type === 'AutoOrganize_ItemUpdated' && data) {

           
            await updateItemStatus(pageGlobal, data);

        } else if (e.type === 'AutoOrganize_ItemAdded' && data) {
           
            await reloadItems(pageGlobal, false);

        } else {

            await reloadItems(pageGlobal, false);

        }
    }

    function updateTaskScheduleLastRun(data) {

        if (data) {
            var last_task_run_header = pageGlobal.querySelector('.last-execution-time');
            var last_run_time = datetime.parseISO8601Date(data.LastExecutionResult.EndTimeUtc, true);
            last_task_run_header.innerHTML = datetime.toLocaleTimeString(last_run_time);
        }
    }

    async function updateItemStatus(page, item) {

        //We won't be updated status in mobile layout.
        var rowId = '#row' + item.Id;
        var row = page.querySelector(rowId);

        if (row) {

            row.innerHTML = await renderTableRow(item);

            try {
                row.querySelector('.btnShowSubtitleList').addEventListener('click', () => {
                    var msg = "";
                    item.Subtitles.forEach(t => {
                        msg += t + '\n';
                    })
                    Dashboard.alert({
                        title: "Subtitles",
                        message: msg
                    });
                });
            } catch (err) {
            }

            try {
                row.querySelector('.btnShowStatusMessage').addEventListener('click',
                    (e) => {
                        let id = e.target.getAttribute('data-resultid');
                        showStatusMessage(id);
                    });
            } catch (err) {}
            try {
                row.querySelector('.btnIdentifyResult').addEventListener('click',
                    (e) => {
                        e.preventDefault();
                        let id = e.target.closest('button').getAttribute('data-resultid');
                        let resultItem = currentResult.Items.filter(function (i) { return i.Id === id; })[0];
                        showCorrectionPopup(e.view, resultItem);
                    });
            } catch (err) {}
            try {
                row.querySelector('.btnProcessResult').addEventListener('click',
                    (e) => {
                        let id = e.target.closest('button').getAttribute('data-resultid');
                        organizeFile(e.view, id);
                    })
            } catch (err) {}
            try {
                row.querySelector('.btnDeleteResult').addEventListener('click',
                    (e) => {
                        let id = e.target.closest('button').getAttribute('data-resultid');
                        deleteOriginalFile(e.view, id);
                    });
            } catch (err) {}

            
            //getLogoImage(item.ExtractedName, item.Type === "Episode" ? "Series" : item.Type).then(logo => {

            //    var td = row.querySelector('.cellName_' + item.Id);
            //    if (logo) {

            //        td.innerHTML = '<img class="" style="background-color:black; padding:1em; border-radius:5px" src="' + logo + '" />';

            //    } else {

            //        td.innerHTML =  html += '<span id="string_name_' + item.Id + '" class="" style="background-color:black; padding:1em; color: white; font-size:1em">' + formatItemName(item.ExtractedName ?? "") + '</span>';

            //    }
            //});

        }
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

    function processSvg(next) {
        switch (next) {
            case 1: return "M12 20C16.42 20 20 16.42 20 12S16.42 4 12 4 4 7.58 4 12 7.58 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.47 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 7.8L12.3 13H11V7H12.5V9.65L14 7.05L15.3 7.8Z"
            case 2: return "M12 20C16.42 20 20 16.42 20 12S16.42 4 12 4 4 7.58 4 12 7.58 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.47 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 13V13H11V7H12.5V11.26L16.2 9.13L16.95 10.43L12.5 13Z"
            case 3: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M17 11.5V13H11V7H12.5V11.5H17Z"
            case 4: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M17 13.9L16.3 15.2L11 12.3V7H12.5V11.4L17 13.9Z"
            case 5: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 16.2L14 17L11 11.8V7H12.5V11.4L15.3 16.2Z"
            case 6: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 7V17H11V7H12.5Z"
            case 7: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 7V12.2L9.8 17L8.5 16.2L11 11.8V7H12.5Z"
            case 8: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 12.8L7.7 15.6L7 14.2L11 11.9V7H12.5V12.8Z"
            case 9: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 7V13H7V11.5H11V7H12.5Z"
            case 10: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 13H11L7 10.7L7.8 9.4L11.1 11.3V7H12.6V13Z"
            case 11: return "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 7V13H11L8.5 8.6L9.8 7.8L11 10V7H12.5Z"
            case 12: return "M12 20C16.42 20 20 16.42 20 12S16.42 4 12 4 4 7.58 4 12 7.58 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.47 22 2 17.5 2 12C2 6.5 6.5 2 12 2M12.5 13.03H11V7H12.5V13.03Z"
        }
    }

    const debounceSearchTerm = debounce(async (text) => {
        await reloadItems(pageGlobal, false, text);
    })

    function debounce(callback, delay = 1000) {
        let timeout

        return (...args) => {
            clearTimeout(timeout)
            timeout = setTimeout(() => {
                callback(...args)
            }, delay)
        }
    }

    var processingAnimation;

    return function (view, params) {

        pageGlobal = view;
        
        var sortByDateBtn = view.querySelector('.date_sort')
        var sortByNameBtn = view.querySelector('.name_sort')
        var sortByStatusBtn = view.querySelector('.status_sort')
        var txtSearch = view.querySelector('#txtSearch');

        processingAnimation = setInterval(() => {
                view.querySelectorAll('.statusIcon').forEach(svg => {
                    if (svg.dataset.active === "true") {
                        var step = parseInt(svg.dataset.step);
                        if (step > 12) {
                            step = 1;
                        }
                        var draw = processSvg(step);
                        var path = svg.querySelector('path');
                        path.setAttribute("d", draw)
                        step += 1;
                        svg.setAttribute('data-step', step)
                    }
                })
            },
            1000);


        view.querySelector('.btnAll').addEventListener('click', async () => {
            query.StartIndex = 0;
            query.Type = "All";
            await reloadItems(view, true);
        });

        view.querySelector('.btnMovie').addEventListener('click', async () => {
            query.StartIndex = 0;
            query.Type = "Movie";
            await reloadItems(view, true);
        });

        view.querySelector('.btnEpisode').addEventListener('click', async () => {
            query.StartIndex = 0;
            query.Type = "Episode";
            await reloadItems(view, true);
        });

        view.querySelector('.btnClearLog').addEventListener('click', function () {

            ApiClient.clearOrganizationLog().then(async function () {
                query.StartIndex = 0;
                await reloadItems(view, true);
            }, Dashboard.processErrorResponse);
        });

        view.querySelector('.btnClearCompleted').addEventListener('click', function () {

            ApiClient.clearOrganizationCompletedLog().then(async function () {
                query.StartIndex = 0;
                await reloadItems(view, true);
            }, Dashboard.processErrorResponse);
        });

       
        txtSearch.addEventListener('input', (e) => {
            debounceSearchTerm(e.target.value);
        });

        //Sort by name column header
        sortByNameBtn.addEventListener('click', async function (e) {
            e.preventDefault();
            if (query.SortBy === 'ExtractedName') {
                query.Ascending = !query.Ascending;
            } else {
                query.SortBy = 'ExtractedName';
                query.Ascending = false;
            }
            await reloadItems(view, false)
        })
        
        //Sort by status column header, and directional arrow
        sortByStatusBtn.addEventListener('click', async function (e) {
            e.preventDefault();
            if (query.SortBy === 'Status') {
                query.Ascending = !query.Ascending;
            } else {
                query.SortBy = 'Status';
                query.Ascending = false;
            }
            await reloadItems(view, false)
        })
       

        //Sort by date column header, and directional arrow
        sortByDateBtn.addEventListener('click', async function (e) {
            e.preventDefault();
            if (query.SortBy === 'OrganizationDate') {
                query.Ascending = !query.Ascending;
            } else {
                query.SortBy = 'OrganizationDate';
                query.Ascending = false;
            }
            await reloadItems(view, false)
        })
        
        var taskButton;

        view.addEventListener('viewshow', async function () {

            const correction = await ApiClient.getFilePathCorrections();
            addCorrectionsTab = correction.Items.length > 0;
            mainTabsManager.setTabs(this, 0, getTabs);

           
                 
            events.on(serverNotifications, 'AutoOrganize_LogReset', await onServerEvent);
            events.on(serverNotifications, 'AutoOrganize_ItemUpdated', await onServerEvent);
            events.on(serverNotifications, 'AutoOrganize_ItemRemoved', await onServerEvent);
            events.on(serverNotifications, 'AutoOrganize_ItemAdded', await onServerEvent);
            events.on(serverNotifications, 'ScheduledTasksInfoStop', await onServerEvent);
            events.on(serverNotifications, 'TaskData', await onServerEvent)
            events.on(serverNotifications, 'TaskComplete', await onServerEvent)
            // on here
            
            try {

                await require(['scripts/taskbutton'], (btn) => taskButton = btn);

            } catch (err) {

                await require(['components/taskbutton'], (btn) => taskButton = btn.default);
            }
            if (taskButton) {
                taskButton({
                    mode: 'on',
                    progressElem: view.querySelector('.itemProgressBar'),
                    panel: view.querySelector('.organizeProgress'),
                    taskKey: 'AutoOrganize',
                    button: view.querySelector('.btnOrganize')
                });
            } 

            ApiClient.getScheduledTask().then(tasks => {
                var data = tasks.filter(t => t.Key == 'AutoOrganize')[0];
                updateTaskScheduleLastRun(data);
            })

            try {
                await reloadItems(view, true);
            } catch (err) {
                loading.hide();
            }
        });

        view.addEventListener('viewhide', function (e) {

            currentResult = null;

            events.off(serverNotifications, 'AutoOrganize_LogReset', onServerEvent);
            events.off(serverNotifications, 'AutoOrganize_ItemUpdated', onServerEvent);
            events.off(serverNotifications, 'AutoOrganize_ItemRemoved', onServerEvent);
            events.off(serverNotifications, 'AutoOrganize_ItemAdded', onServerEvent);
            events.off(serverNotifications, 'ScheduledTaskEnded', onServerEvent);
            events.off(serverNotifications, 'TaskData', onServerEvent)
            events.off(serverNotifications, 'TaskComplete', onServerEvent)

            // off here
            taskButton({
                mode: 'off',
                button: view.querySelector('.btnOrganize')
            });
        });
    };
});