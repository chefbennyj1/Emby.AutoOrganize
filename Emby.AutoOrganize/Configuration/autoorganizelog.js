define(['globalize', 'serverNotifications', 'events', 'scripts/taskbutton', 'datetime', 'loading', 'mainTabsManager', 'dialogHelper', 'paper-icon-button-light', 'formDialogStyle','emby-linkbutton', 'detailtablecss', 'emby-collapse'], function (globalize, serverNotifications, events, taskButton, datetime, loading, mainTabsManager, dialogHelper) {
    'use strict';

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

        //Only one option: RequestToOverwriteFile = true
        var url = this.getUrl("Library/FileOrganizations/" + id + "/Organize");

        return this.ajax({
            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: 'application/json'
        });
    };
    //
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

    var query = {

        StartIndex: 0,
        Limit: 50
    };

    var currentResult;
    var pageGlobal;
    var sort = {
        type: 'date',
        ascending: true,
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

                ApiClient.deleteOriginalFileFromOrganizationResult(id).then(function () {

                    loading.hide();

                    reloadItems(page, true);

                }, Dashboard.processErrorResponse);
            });
        });
    }

    function organizeFileWithCorrections(page, item) {

        showCorrectionPopup(page, item);
    }

    function showCorrectionPopup(page, item) {

        require([Dashboard.getConfigurationResourceUrl('FileOrganizerJs')], function (fileorganizer) {

            fileorganizer.show(item).then(function () {
                reloadItems(page, false);
            },
                function () { /* Do nothing on reject */ });
        });
    }

    function organizeFile(page, id) {

        var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];

        //TODO - make sure item has a target path
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

        html += '<div class="formDialogContent" style="text-align:center; display:flex; justify-content:center;align-items:center">';
        html += '<svg style="width: 55px;height: 55px;top: 19%;position: absolute;" viewBox="0 0 24 24"><path fill="var(--theme-primary-color)" d="M21 11.1V8C21 6.9 20.1 6 19 6H11L9 4H3C1.9 4 1 4.9 1 6V18C1 19.1 1.9 20 3 20H10.2C11.4 21.8 13.6 23 16 23C19.9 23 23 19.9 23 16C23 14.1 22.2 12.4 21 11.1M9.3 18H3V8H19V9.7C18.1 9.2 17.1 9 16 9C12.1 9 9 12.1 9 16C9 16.7 9.1 17.4 9.3 18M16 21C13.2 21 11 18.8 11 16S13.2 11 16 11 21 13.2 21 16 18.8 21 16 21M17 14H15V12H17V14M17 20H15V15H17V20Z"></path></svg>';
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
                    RequestToMoveFile: true,
                    Id: item.Id
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
    }

    function openOverviewDialog() {
        var result = currentResult;
        var dlg = dialogHelper.createDialog({
            size: "small",
            removeOnClose: !1,
            scrollY: !0
        });

        dlg.classList.add("formDialog");
        dlg.classList.add("ui-body-a");
        dlg.classList.add("background-theme-a");
       


        var html = '';
        html += '<div class="formDialogHeader" style="display:flex">';
        html += '<button is="paper-icon-button-light" class="btnCloseDialog autoSize paper-icon-button-light" tabindex="-1"><i class="md-icon"></i></button><h3 class="formDialogHeaderTitle"></h3>';
        html += '</div>';

        html += '<div class="formDialogContent" style="position: relative;display: flex;justify-content: center;">';

        html += '<div style="height:50%; width:50%">'
        html += '<canvas id="sortingStats" width="100" height="100"></canvas>';
        html += '</div>';

        html += '</div>';

        dlg.innerHTML = html;

        dlg.querySelector('.btnCloseDialog').addEventListener('click',
            () => {
                dialogHelper.close(dlg);
            });

        var style = getComputedStyle(document.body);
        var successCount    = Math.floor(result.Items.filter(i => i.Status == "Success").length / result.Items.length * 100);
        var failureCount    = Math.floor(result.Items.filter(i => i.Status == "Failure").length  / result.Items.length * 100);
        var skippedCount    = Math.floor(result.Items.filter(i => i.Status == "SkippedExisting").length  / result.Items.length * 100);
        var processingCount = Math.floor(result.Items.filter(i => i.Status == "Processing").length  / result.Items.length * 100);
        var inUse           = Math.floor(result.Items.filter(i => i.Status == "InUse").length  / result.Items.length * 100);

        require([Dashboard.getConfigurationResourceUrl('Chart.js')], (Chart) => {

            var progressCtx = dlg.querySelector('#sortingStats').getContext("2d");
            new Chart(progressCtx,
                {
                    type: 'doughnut',
                    label: "Status",
                    data: {
                        labels  : [ 'Success', 'Failure', "Skipped/Existing", "Processing", "In Use" ],
                        datasets: [
                            {
                                data: [successCount, failureCount, skippedCount, processingCount, inUse ],
                                backgroundColor: ["green", "red", "goldenrod", style.getPropertyValue("--theme-primary-color"), "black"],
                                borderColor: ["black", style.getPropertyValue("--theme-primary-color")],
                                borderWidth: 1,
                                //dataFriendly   : [ driveData[t].FriendlyUsed, driveData[t].FriendlyAvailable ]
                            }
                        ]
                    },
                    options: {
                        cutoutPercentage: 0, 
                        legend: { position: "left" }
                    }
                });

        });

        dialogHelper.open(dlg);
    }

    function reloadItems(page, showSpinner, searchTerm = "") {

        if (showSpinner) {
            loading.show();
        }

        //Search Term from Search Box.
        query.NameStartsWith = encodeURI(searchTerm);

        ApiClient.getFileOrganizationResults(query).then(function (result) {
            currentResult = result;
            renderResults(page, result);
            
            loading.hide();
        });

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

    function sortListResultsByDate(items) {
        return items.sort((a, b) => {
            var da = new Date(datetime.parseISO8601Date(a.Date, true)),
                db = new Date(datetime.parseISO8601Date(b.Date, true))
            return sort.ascending ? da + db : da - db;            
        })
    }

    function sortListResultItemsByName(items) {
        return items.sort((a, b) => {
            var fa = a.ExtractedName.toLowerCase(),
                fb = b.ExtractedName.toLowerCase();
            return sort.ascending ? fa < fb ? -1 : fa > fb ? 1 : 0 : fb < fa ? -1 : fb > fa ? 1 : 0;
        })
    }

    function sortListResultItemsByStatus(items) {
        return items.sort((a, b) => {
            var fa = a.Status,
                fb = b.Status;
            return sort.ascending ? fa < fb ? -1 : fa > fb ? 1 : 0 : fb < fa ? -1 : fb > fa ? 1 : 0;            
        })
    }
         
    function renderResults(page, result) {

        var items;
        switch (sort.type) {
            case "date":
                items = sortListResultsByDate(result.Items);
                //if we're sorted by date show the arrow icon
                pageGlobal.querySelector('.date_sort > svg').style.opacity = 1;
                break;
            case "status":
               //if we're sorted by status show the arrow icon
                pageGlobal.querySelector('.status_sort > svg').style.opacity = 1;
                items = sortListResultItemsByStatus(result.Items);
                break;
            case "name":
                //if we're sorted by name show the arrow icon
                pageGlobal.querySelector('.name_sort > svg').style.opacity = 1;
                items = sortListResultItemsByName(result.Items);
                break;
        }

        if (Object.prototype.toString.call(page) !== "[object Window]") {
            var rows = '';
            items.forEach(item => {
                var html = '';
               
                html += '<tr class="detailTableBodyRow detailTableBodyRow-shaded" id="row' + item.Id + '">';
               
                html += renderItemRow(item, page);
              
                html += '</tr>';                

                rows += html;
            })

            var resultBody = page.querySelector('.resultBody');
            resultBody.innerHTML = rows;

            resultBody.addEventListener('click', handleItemClick);

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
                btnNextTop.addEventListener('click', function () {
                    query.StartIndex += query.Limit;
                    reloadItems(page, true);
                });
            }

            if (btnNextBottom) {
                btnNextBottom.addEventListener('click', function () {
                    query.StartIndex += query.Limit;
                    reloadItems(page, true);
                });
            }

            if (btnPrevTop) {
                btnPrevTop.addEventListener('click', function () {
                    query.StartIndex -= query.Limit;
                    reloadItems(page, true);
                });
            }

            if (btnPrevBottom) {
                btnPrevBottom.addEventListener('click', function () {
                    query.StartIndex -= query.Limit;
                    reloadItems(page, true);
                });
            }
            
        }
        
        


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
                color: 'var(--theme-text-color)'
            }
            case 'DeleteBtn': return {
                path: "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z",
                color: 'var(--theme-text-color)'
            };
            case 'ProcessBtn': return {
                path: "M4 7C4 4.79 7.58 3 12 3S20 4.79 20 7 16.42 11 12 11 4 9.21 4 7M19.72 13.05C19.9 12.71 20 12.36 20 12V9C20 11.21 16.42 13 12 13S4 11.21 4 9V12C4 14.21 7.58 16 12 16C12.65 16 13.28 15.96 13.88 15.89C14.93 14.16 16.83 13 19 13C19.24 13 19.5 13 19.72 13.05M13.1 17.96C12.74 18 12.37 18 12 18C7.58 18 4 16.21 4 14V17C4 19.21 7.58 21 12 21C12.46 21 12.9 21 13.33 20.94C13.12 20.33 13 19.68 13 19C13 18.64 13.04 18.3 13.1 17.96M23 19L20 16V18H16V20H20V22L23 19Z",
                color: 'var(--theme-text-color)'
            }
        }
    }

    function getStatusRenderData(status) {
        switch (status) {
            case 'Success': return {
                path: "M10,17L5,12L6.41,10.58L10,14.17L17.59,6.58L19,8M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                color: "green",
                text: "Complete"
            };
            case 'Failure': return {
                path: "M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z",
                color: "orangered",
                text: "Attention - Unidentified"
            };
            case 'SkippedExisting': return {
                path: "M13 14H11V9H13M13 18H11V16H13M1 21H23L12 2L1 21Z",
                color: "goldenrod",
                text: "Attention - Existing Item"
            };
            case 'Processing': return {
                path: "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 16.2L14 17L11 11.8V7H12.5V11.4L15.3 16.2Z",
                color: "var(--theme-accent-text-color)",
                text: "Processing..."
            };
            case "Checking": return {
                path: "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 16.2L14 17L11 11.8V7H12.5V11.4L15.3 16.2Z",
                color: "goldenrod",
                text: "Checking..."
            };
            case "NewResolution": return {
                path: "M12 5.5L10 8H14L12 5.5M18 10V14L20.5 12L18 10M6 10L3.5 12L6 14V10M14 16H10L12 18.5L14 16M21 3H3C1.9 3 1 3.9 1 5V19C1 20.1 1.9 21 3 21H21C22.1 21 23 20.1 23 19V5C23 3.9 22.1 3 21 3M21 19H3V5H21V19Z",
                color: "var(--theme-accent-text-color)",
                text: "New Resolution Available"
            }
            case "NotEnoughDiskSpace": return {
                path: "",
                color: "orangered",
                text: "Attention - Not Enough Disk Space!"
            }
             case "InUse": return {
                path: "M22 12C22 6.46 17.54 2 12 2C10.83 2 9.7 2.19 8.62 2.56L9.32 4.5C10.17 4.16 11.06 3.97 12 3.97C16.41 3.97 20.03 7.59 20.03 12C20.03 16.41 16.41 20.03 12 20.03C7.59 20.03 3.97 16.41 3.97 12C3.97 11.06 4.16 10.12 4.5 9.28L2.56 8.62C2.19 9.7 2 10.83 2 12C2 17.54 6.46 22 12 22C17.54 22 22 17.54 22 12M5.47 7C4.68 7 3.97 6.32 3.97 5.47C3.97 4.68 4.68 3.97 5.47 3.97C6.32 3.97 7 4.68 7 5.47C7 6.32 6.32 7 5.47 7M9 9H11V15H9M13 9H15V15H13",
                color: "goldenrod",
                text: "Target file currently in use"
            }
            case 'Waiting': return {
                path: "M12 20C16.4 20 20 16.4 20 12S16.4 4 12 4 4 7.6 4 12 7.6 20 12 20M12 2C17.5 2 22 6.5 22 12S17.5 22 12 22C6.5 22 2 17.5 2 12C2 6.5 6.5 2 12 2M15.3 16.2L14 17L11 11.8V7H12.5V11.4L15.3 16.2Z",
                color: "goldenrod",
                text: "Awaiting user input..."
            };
        }
    }

    //function showTableOptionDialog(view) {
    //    var dlg = dialogHelper.createDialog({
    //        removeOnClose: true,
    //        size: 'small'
    //    });

    //    dlg.classList.add('ui-body-a');
    //    dlg.classList.add('background-theme-a');

    //    dlg.classList.add('formDialog');
    //    dlg.style.maxWidth = '18%'
    //    dlg.style.maxHeight = '20%'
    //    var html = '';

    //    html += '<div class="formDialogHeader">'
    //    html += '<button is="paper-icon-button-light" class="btnCancel autoSize" tabindex="-1"><i class="md-icon">&#xE5C4;</i></button>'
    //    html += '<h3 class="formDialogHeaderTitle">Table Options</h3>'
    //    html += '</div>'

    //    html += '<div class="formDialogContent" style="margin:2em">'
    //    html += '<div class="dialogContentInner" style="max-width: 100%; max-height:100%; display: flex;align-items: center;justify-content: center;">'

    //    html += '<button is="emby-button" type="button" class="btnClearCompleted raised button-cancel">'
    //    html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">'
    //    html += '<path fill="currentColor" d="M18 14.5C19.11 14.5 20.11 14.95 20.83 15.67L22 14.5V18.5H18L19.77 16.73C19.32 16.28 18.69 16 18 16C16.62 16 15.5 17.12 15.5 18.5C15.5 19.88 16.62 21 18 21C18.82 21 19.55 20.61 20 20H21.71C21.12 21.47 19.68 22.5 18 22.5C15.79 22.5 14 20.71 14 18.5C14 16.29 15.79 14.5 18 14.5M4 3H18C19.11 3 20 3.9 20 5V12.17C19.5 12.06 19 12 18.5 12C17.23 12 16.04 12.37 15.04 13H12V17H12.18C12.06 17.5 12 18 12 18.5L12 19H4C2.9 19 2 18.11 2 17V5C2 3.9 2.9 3 4 3M4 7V11H10V7H4M12 7V11H18V7H12M4 13V17H10V13H4Z" />'
    //    html += '</svg>'
    //    html += '<span>Clear Completed</span>'
    //    html += '</button>'

    //    html += '<button is="emby-button" type="button" class="btnClearLog raised button-cancel">'
    //    html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">'
    //    html += '<path fill="currentColor" d="M15.46,15.88L16.88,14.46L19,16.59L21.12,14.46L22.54,15.88L20.41,18L22.54,20.12L21.12,21.54L19,19.41L16.88,21.54L15.46,20.12L17.59,18L15.46,15.88M4,3H18A2,2 0 0,1 20,5V12.08C18.45,11.82 16.92,12.18 15.68,13H12V17H13.08C12.97,17.68 12.97,18.35 13.08,19H4A2,2 0 0,1 2,17V5A2,2 0 0,1 4,3M4,7V11H10V7H4M12,7V11H18V7H12M4,13V17H10V13H4Z" />'
    //    html += '</svg>'
    //    html += '<span>Clear All</span>'
    //    html += '</button>'
    //    html += '</div>'
    //    html += '</div>'

    //    dlg.innerHTML = html
        

    //    dlg.querySelector('.btnCancel').addEventListener('click', (e) => {
    //        dialogHelper.close(dlg);
    //    })

    //    dlg.querySelector('.btnClearLog').addEventListener('click', function () {

    //        ApiClient.clearOrganizationLog().then(function () {
    //            query.StartIndex = 0;
    //            reloadItems(view, true);
    //            dialogHelper.close(dlg);
    //        }, Dashboard.processErrorResponse);
    //    });

    //    dlg.querySelector('.btnClearCompleted').addEventListener('click', function () {

    //        ApiClient.clearOrganizationCompletedLog().then(function () {
    //            query.StartIndex = 0;
    //            reloadItems(view, true);
    //            dialogHelper.close(dlg);
    //        }, Dashboard.processErrorResponse);
    //    });

    //    dialogHelper.open(dlg);
    //}

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
        }
        
    }
    
    function renderItemRow(item, page) {
        
        var html = '';
        var statusRenderData = item.IsInProgress && item.Status !== "Processing" && item.Status !== "Failure" //We're in some kind of progress, but not processing, or failing. We must be 'checking'
            ? getStatusRenderData("Checking") 
            : item.IsInProgress && item.Status === "Failure" //We failed before, but now we are processing. 
            ? getStatusRenderData("Processing") 
            : getStatusRenderData(item.Status); //The actual status icon
        
        //Progress Status Icon
        html += '<td class="detailTableBodyCell">';
        html += '<div class="progressIcon">';
        html += '<svg id="statusIcon" style="width:24px;height:24px" viewBox="0 0 24 24">';
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
        html += '<td data-resultid="' + item.Id + '" class= class="detailTableBodyCell fileCell">';
        html += '<span>' + statusRenderData.text + '</span>';
        html += '</td>';

        //Name
        html += '<td data-resultid="' + item.Id + '" class= class="detailTableBodyCell fileCell">';
        html += '<span>' + formatItemName(item.ExtractedName ?? "") + '</span>';
        html += '</td>';             

        //Release Edition
        html += '<td class="detailTableBodyCell fileCell" data-title="Edition">';
        switch(item.Type) {
            case "Episode":
                if (item.ExtractedSeasonNumber && item.ExtractedEpisodeNumber) {
                    html += '<span>' + item.ExtractedSeasonNumber + 'x' + (item.ExtractedEpisodeNumber <= 9 ? `0${item.ExtractedEpisodeNumber}` : item.ExtractedEpisodeNumber) + '</span>';
                } else {
                    html += '';
                }
            case "Movie":
                html += '<span>' + (item.ExtractedEdition ?? "") + '</span>';  
        }
       
        html += '</td>';

        //Resolution
        html += '<td class="detailTableBodyCell fileCell" data-title="Resolution">';
        html += '<span>' + (item.ExtractedResolution ?? "")  + '</span>';  
        html += '</td>';

        //File Size
        html += '<td class="detailTableBodyCell fileCell" data-title="File Size">';
        html += '<span>' + formatBytes(item.FileSize) + '</span>';
        html += '</td>';        

        //Media Type Icon (Movie/Episode) / Progress Bar
        var icon = getResultItemTypeIcon(item.Type)
        html += '<td class="detailTableBodyCell">';
        html += '<div class="type-icon-container">';
        html += '<svg id="typeIcon" style="width:24px;height:24px" viewBox="0 0 24 24">';
        html += '<path fill="' + statusRenderData.color + '" d="' + icon.path + '"/>';
        html += '</svg>';
        html += '</div>';
        html += '</td>';

        //Source
        html += '<td data-title="Source" class="detailTableBodyCell fileCell">';
        html += '<a is="emby-linkbutton" data-resultid="' + item.Id + '" style="color:' + statusRenderData.color + ';" href="#" class="button-link btnShowStatusMessage">';
        html += item.OriginalFileName;
        html += '</a>';
        html += '</td>';

        //Destination
        html += '<td data-title="Destination" class="detailTableBodyCell fileCell">';
        html += item.TargetPath || '';
        html += '</td>';

        //Row sorting options (action buttons)
        html += '<td class="detailTableBodyCell organizerButtonCell" data-title="Actions" style="whitespace:no-wrap;">';
        if (item.Status == "Checking" || item.Status == "InUse") {
            html += '';
        } else {
            if (item.Status !== 'Success') {

                if (item.Status !== "Processing") {
                    //Identify Entry Button - This opens the Identify/Lookup modal for the item.
                    //We want to show this option if the item has been skipped because we think it alrerady exists, or the item failed to find a match.
                    //There is a chance that the Lookup was incorrect if there was a match to an existing item.
                    //Allow the user to identify the item.
                    if (item.Status === "Failure") {
                        var identifyBtn = getButtonSvgIconRenderData("IdentifyBtn");
                        html += '<button type="button" is="paper-icon-button-light" data-resultid="' + item.Id + '" data-type="' + item.Type + '" class="btnIdentifyResult organizerButton autoSize" title="Identify">';
                        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
                        html += '<path fill="' + identifyBtn.color + '" d="' + identifyBtn.path + '"/>';
                        html += '</svg>';
                        html += '</button>';
                    }

                    //Process Entry Button - This will process the item into the library based on the info in the "Destination" column of the table row.
                    //Only show this button option if: it is not a Success, not Processing, and has not failed to find a possible result.
                    //The "Destination" column info will be populated.
                    if (item.Status !== "Failure" || item.Status == "NewResolution" || item.Status === "SkippedExisting" && item.TargetPath) {
                        var processBtn = getButtonSvgIconRenderData("ProcessBtn");
                        html += '<button type="button" is="paper-icon-button-light" data-resultid="' + item.Id + '" class="btnProcessResult organizerButton autoSize" title="Organize">';
                        html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
                        html += '<path fill="' + processBtn.color + '" d="' + processBtn.path + '"/>';
                        html += '</svg>';
                        html += '</button>';
                    }
                }

                //Delete Entry Button - This deletes the item from the log window and removes it from watched folder - always show this option
                var deleteBtn = getButtonSvgIconRenderData("DeleteBtn");
                html += '<button type="button" is="paper-icon-button-light" data-resultid="' + item.Id + '" class="btnDeleteResult organizerButton autoSize" title="Delete">';
                html += '<svg style="width:24px;height:24px" viewBox="0 0 24 24">';
                html += '<path fill="' + deleteBtn.color + '" d="' + deleteBtn.path + '"/>';
                html += '</svg>';
                html += '</button>';
                html += '</td>';

            }
            
        }

        return html;
    }

    function handleItemClick(e) {

        var id;

        var buttonStatus = parentWithClass(e.target, 'btnShowStatusMessage');
        if (buttonStatus) {

            id = buttonStatus.getAttribute('data-resultid');
            showStatusMessage(id);
        }

        var identifyOrganize = parentWithClass(e.target, "btnIdentifyResult");
        if (identifyOrganize) {
            id = identifyOrganize.getAttribute('data-resultid');
            var item = currentResult.Items.filter(function (i) { return i.Id === id; })[0];
            showCorrectionPopup(e.view, item);
        }

        var buttonOrganize = parentWithClass(e.target, 'btnProcessResult');
        if (buttonOrganize) {
            id = buttonOrganize.getAttribute('data-resultid');
            organizeFile(e.view, id);
        }

        var buttonDelete = parentWithClass(e.target, 'btnDeleteResult');
        if (buttonDelete) {

            id = buttonDelete.getAttribute('data-resultid');
            deleteOriginalFile(e.view, id);
        }

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

    function onServerEvent(e, apiClient, data) {
        if (e.type === "TaskCompleted") {
            if (data && data == 'AutoOrganize') {
                reloadItems(pageGlobal, false);
               
            }
        }
        if (e.type === 'ScheduledTaskEnded') {

            if (data && data.ScheduledTask.Key === 'AutoOrganize') {
                reloadItems(pageGlobal, false);
            }

        } else if (e.type === 'TaskData') {

            if (data && data.ScheduledTask.Key === 'AutoOrganize') {
                updateTaskScheduleLastRun(data);
                reloadItems(pageGlobal, false);
                //checkUpdatingTableItems(pageGlobal);
            }

        } else if (e.type === 'AutoOrganize_ItemUpdated' && data) {

            updateItemStatus(pageGlobal, data);

        } else if (e.type === 'AutoOrganize_ItemAdded' && data) {

            reloadItems(pageGlobal, false);

        } else {

            reloadItems(pageGlobal, false);

        }
    }

    function updateTaskScheduleLastRun(data) {

        if (data) {
            var last_task_run_header = pageGlobal.querySelector('.last-execution-time');
            var last_run_time = datetime.parseISO8601Date(data.LastExecutionResult.EndTimeUtc, true);
            last_task_run_header.innerHTML = "Task last run: " + datetime.toLocaleTimeString(last_run_time);
        }
    }

    function updateItemStatus(page, item) {

        var rowId = '#row' + item.Id;
        var row = page.querySelector(rowId);

        if (row) {

            row.innerHTML = renderItemRow(item, page);

        }
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

    const debounceSearchTerm = debounce((text) => {
        reloadItems(pageGlobal, false, text);
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

    return function (view, params) {

        pageGlobal = view;
        //view.querySelector('.btnOpenTableOptionsBtn').addEventListener('click', (e) => {
        //     showTableOptionDialog(view);
        //});
        
        var sortByDateBtn = view.querySelector('.date_sort')
        var sortByNameBtn = view.querySelector('.name_sort')
        var sortByStatusBtn = view.querySelector('.status_sort')
        var txtSearch = view.querySelector('#txtSearch');

        view.querySelector('.btnClearLog').addEventListener('click', function () {

            ApiClient.clearOrganizationLog().then(function () {
                query.StartIndex = 0;
                reloadItems(view, true);
            }, Dashboard.processErrorResponse);
        });

        view.querySelector('.btnClearCompleted').addEventListener('click', function () {

            ApiClient.clearOrganizationCompletedLog().then(function () {
                query.StartIndex = 0;
                reloadItems(view, true);
            }, Dashboard.processErrorResponse);
        });

        view.querySelector('.btnResultOverview').addEventListener('click', () => {
            openOverviewDialog();
        });
        txtSearch.addEventListener('input', (e) => {
            debounceSearchTerm(e.target.value);
        });
        //Sort by name column header, and directional arrow
        sortByNameBtn.addEventListener('click', function (e) {
            e.preventDefault();
            sort = { type: 'name', ascending: sort.type == "name" ? sort.ascending ? false : true : true };
            var path = sortByNameBtn.querySelector('path')

            //Swap the arrow svg icon to show the arrow pointing up or down based on ascending or descending
            !sort.ascending ? path.setAttribute('d', "M7,15L12,10L17,15H7Z") :
                path.setAttribute('d', "M7,10L12,15L17,10H7Z")

            //Turn off the arrows on the other sorting options
            sortByDateBtn.querySelector('svg').style.opacity = 0
            sortByStatusBtn.querySelector('svg').style.opacity = 0

            reloadItems(view, false)
        })
        sortByNameBtn.addEventListener('mouseenter', (e) => {
            var svg = sortByNameBtn.querySelector('svg')
            svg.style.opacity = 1;
        })
        sortByNameBtn.addEventListener('mouseleave', (e) => {
            //Only hide the arrow on mouse leave if we are not sorted by this type
            if (sort.type !== "name") {
                var svg = sortByNameBtn.querySelector('svg')
                svg.style.opacity = 0;
            }
        })

        //Sort by status column header, and directional arrow
        sortByStatusBtn.addEventListener('click', function (e) {
            e.preventDefault();
            sort = { type: 'status', ascending: sort.type == "status" ? sort.ascending ? false : true : true };
            var path = sortByStatusBtn.querySelector('path')

            //Swap the sort arrow svg icon to show the arrow pointing up or down based on ascening or decending
            !sort.ascending ? path.setAttribute('d', "M7,15L12,10L17,15H7Z") :
                path.setAttribute('d', "M7,10L12,15L17,10H7Z")

            //Turn off the arrows on the other sorting options
            sortByDateBtn.querySelector('svg').style.opacity = 0
            sortByNameBtn.querySelector('svg').style.opacity = 0

            reloadItems(view, false)
        })
        sortByStatusBtn.addEventListener('mouseenter', (e) => {
            var svg = sortByStatusBtn.querySelector('svg')
            svg.style.opacity = 1;
        })
        sortByStatusBtn.addEventListener('mouseleave', (e) => {
            //Only hide the arrow on mouse leave if we are not sorted by this type
            if (sort.type !== "status") {
                var svg = sortByStatusBtn.querySelector('svg')
                svg.style.opacity = 0;
            }
        })

        //Sort by date column header, and directional arrow
        sortByDateBtn.addEventListener('click', function (e) {
            e.preventDefault();
            sort = { type: 'date', ascending: sort.type == "date" ? sort.ascending ? false : true : true };
            var path = sortByDateBtn.querySelector('path')

            //Swap the sort arrow svg icon to show the arrow pointing up or down based on ascening or decending
            !sort.ascending ? path.setAttribute('d', "M7,15L12,10L17,15H7Z") :
                path.setAttribute('d', "M7,10L12,15L17,10H7Z")

            //Turn off the arrows on the other sorting options
            sortByStatusBtn.querySelector('svg').style.opacity = 0
            sortByNameBtn.querySelector('svg').style.opacity = 0

            reloadItems(view, false)
        })
        sortByDateBtn.addEventListener('mouseenter', (e) => {
            var svg = sortByDateBtn.querySelector('svg')
            svg.style.opacity = 1;
        })
        sortByDateBtn.addEventListener('mouseleave', (e) => {
            //Only hide the arrow on mouse leave if we are not sorted by this type
            if (sort.type !== "date") {
                var svg = sortByDateBtn.querySelector('svg')
                svg.style.opacity = 0;
            }
        })
        
        view.addEventListener('viewshow', function (e) {

            mainTabsManager.setTabs(this, 0, getTabs);

            reloadItems(view, true);
                 
            events.on(serverNotifications, 'AutoOrganize_LogReset', onServerEvent);
            events.on(serverNotifications, 'AutoOrganize_ItemUpdated', onServerEvent);
            events.on(serverNotifications, 'AutoOrganize_ItemRemoved', onServerEvent);
            events.on(serverNotifications, 'AutoOrganize_ItemAdded', onServerEvent);
            events.on(serverNotifications, 'ScheduledTaskEnded', onServerEvent);
            events.on(serverNotifications, 'TaskData', onServerEvent)
            // on here
            taskButton({
                mode: 'on',
                progressElem: view.querySelector('.organizeProgress'),
                panel: view.querySelector('.organizeTaskPanel'),
                taskKey: 'AutoOrganize',
                button: view.querySelector('.btnOrganize')
            });

            ApiClient.getScheduledTask().then(tasks => {
                var data = tasks.filter(t => t.Key == 'AutoOrganize')[0];
                updateTaskScheduleLastRun(data);
            })
        });

        view.addEventListener('viewhide', function (e) {

            currentResult = null;

            events.off(serverNotifications, 'AutoOrganize_LogReset', onServerEvent);
            events.off(serverNotifications, 'AutoOrganize_ItemUpdated', onServerEvent);
            events.off(serverNotifications, 'AutoOrganize_ItemRemoved', onServerEvent);
            events.off(serverNotifications, 'AutoOrganize_ItemAdded', onServerEvent);
            events.off(serverNotifications, 'ScheduledTaskEnded', onServerEvent);
            events.off(serverNotifications, 'TaskData', onServerEvent)

            // off here
            taskButton({
                mode: 'off',
                button: view.querySelector('.btnOrganize')
            });
        });
    };
});