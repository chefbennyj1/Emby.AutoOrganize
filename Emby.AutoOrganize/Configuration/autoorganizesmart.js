define(['loading', 'mainTabsManager', 'globalize', 'dialogHelper', 'formDialogStyle', 'listViewStyle', 'emby-input', 'emby-select', 'emby-checkbox', 'emby-button', 'emby-collapse', 'emby-toggle'],
    function (loading, mainTabsManager, globalize, dialogHelper, formDialogStyle) {
        'use strict';

        ApiClient.getFileOrganizationResults = function (options) {

            var url = this.getUrl("Library/FileOrganization", options || {});

            return this.getJSON(url);
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

        ApiClient.saveCustomSmartMatchEntry = function (options) {

            var url = this.getUrl("Library/FileOrganizations/SmartMatches/Save");

            var postData = {
                TargetFolder: options.TargetFolder,
                Matches: options.Matches,
                Type: options.Type,
                Id: options.Id || ""
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
            Limit: 100000
        };

        var currentResult;

        function parentWithClass(elem, className) {

            while (!elem.classList || !elem.classList.contains(className)) {
                elem = elem.parentNode;

                if (!elem) {
                    return null;
                }
            }

            return elem;
        }

        function reloadList(page) {

            loading.show();

            ApiClient.getSmartMatchInfos(query).then(function (result) {

                if (result && result.Items.length) {
                    currentResult = result;

                    populateList(page, result);

                    loading.hide();
                } else {
                    loading.hide();
                }
                },
                function () {

                    loading.hide();
                });
        }

        function getHtmlFromMatchStrings(info, i) {

            var matchStringIndex = 0;

            return info.MatchStrings.map(function (m) {

                var matchStringHtml = '';

                matchStringHtml += '<div class="listItem" style="border-bottom: 1px solid var(--theme-icon-focus-background)">';

                matchStringHtml += '<svg style="width:24px;height:24px" viewBox="0 0 24 24"> ';
                matchStringHtml += '<path fill="var(--theme-accent-text-color-lightbg)" d="M12,6A6,6 0 0,1 18,12C18,14.22 16.79,16.16 15,17.2V19A1,1 0 0,1 14,20H10A1,1 0 0,1 9,19V17.2C7.21,16.16 6,14.22 6,12A6,6 0 0,1 12,6M14,21V22A1,1 0 0,1 13,23H11A1,1 0 0,1 10,22V21H14M20,11H23V13H20V11M1,11H4V13H1V11M13,1V4H11V1H13M4.92,3.5L7.05,5.64L5.63,7.05L3.5,4.93L4.92,3.5M16.95,5.63L19.07,3.5L20.5,4.93L18.37,7.05L16.95,5.63Z" />';
                matchStringHtml += '</svg> ';

                matchStringHtml += '<div class="listItemBody">';

                matchStringHtml += "<div class='listItemBodyText secondary'>";

                matchStringHtml += m;  
                
                matchStringHtml += "</div>";

                matchStringHtml += '</div>';

                matchStringHtml += '<button type="button" is="emby-button" class="btnDeleteMatchEntry emby-button" style="padding: 0;" data-index="' +
                    i +
                    '" data-matchindex="' +
                    matchStringIndex +
                    '" title="Delete"><i class="md-icon">delete</i></button>';

                matchStringHtml += '</div>';
                matchStringIndex++;

                return matchStringHtml;

            }).join('');
        }

        function populateList(page, result) {

            var infos = result.Items;

            if (infos) {
                infos = infos.sort(function (a, b) {

                    a = a.OrganizerType + " " + (a.Name);
                    b = b.OrganizerType + " " + (b.Name);

                    if (a === b) {
                        return 0;
                    }

                    if (a < b) {
                        return -1;
                    }

                    return 1;
                });
            }

            var i = 0;

            var html = "";

            if (infos.length) {

                html += '<div class="" style="padding:4%">';
            }

            var matchInfos = page.querySelector('.divMatchInfos');
            var customMatchInfos = page.querySelector('.divCustomMatchInfos');
            infos.forEach(info => {
                
                if (info.IsCustomUserDefinedEntry) {

                    html += '<div is="emby-collapse" title="' + (info.TargetFolder) + ' ' + info.MatchStrings.join('|') +'">';
                    html += '<div class="collapseContent">';
                    html += getHtmlFromMatchStrings(info, i);
                    html += '</div>';
                    html += '</div>';

                    customMatchInfos.innerHTML += html;

                } else {
                    html += '<div is="emby-collapse" title="' + (info.Name) + '">';
                    html += '<div class="collapseContent">';
                    html += getHtmlFromMatchStrings(info, i);
                    html += '</div>';
                    html += '</div>';

                    matchInfos.innerHTML += html;
                }

                
                i++;

            });


            html += "</div>";
            
           
            
        }

        async function openCustomSmartMatchDialog(view, id = "") {
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
            html += '<button is="paper-icon-button-light" class="btnCloseDialog autoSize paper-icon-button-light" tabindex="-1"><i class="md-icon"></i></button><h3 class="formDialogHeaderTitle">Custom Smart Match</h3>';
            html += '</div>';

            html += '<div class="formDialogContent" style="margin:2em;">';
            
            //File name contains
            html += '<div class="inputContainer">';
            html += '<label class="inputLabel" for="txtFileNameContains">Match names:</label><input is="emby-input" id="txtFileNameContains" required="required" type="text" label="File Name Contains:" class="emby-input">';
            html += '<div class="fieldDescription">Each value separated by ;</div>';
            html += '</div>';

            //Type of media
            html += '<div class="selectContainer">';
            html += '<select is="emby-select" id="selectMediaType" data-mini="true" required="required" label="Media type">';
            html += '<option></option>';
            //html += '<option value="Episode">Series</option>';
            html += '<option value="Movie">Movie</option>';
            html += '</select>';
            html += '</div>';
            
            
            //The root/Target folder
            html += '<div class="selectContainer">';
            html += '<div class="selectRootFolderContainer selectContainer">';
            html += '<select id="selectRootFolder" is="emby-select" label="Root folder:" required="required"></select>';
            html += '</div>';
            html += '</div>';


            html += '<div class="formDialogFooter">';
            html += '<div style="display:flex;align-items:center;justify-content:center">';
            html += '<button id="okButton" is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem emby-button">Ok</button>';
            html += '<button id="cancelButton" is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem emby-button">Cancel</button>';
            html += '</div>';
            html += '</div>';


            html += '</div>';

            dlg.innerHTML = html;

            var targetFolderSelect = dlg.querySelector('#selectRootFolder');
            var organizerTypeSelect = dlg.querySelector('#selectMediaType');
            var fileNameContainsInput = dlg.querySelector('#txtFileNameContains');

            const virtualFolderResult = await ApiClient.getVirtualFolders();

            organizerTypeSelect.addEventListener('change',
                () => {
                    if (organizerTypeSelect.value != '') {
                        var virtualFolders = virtualFolderResult.Items; //|| result;
                        for (var n = 0; n < virtualFolders.length; n++) {

                            var virtualFolder = virtualFolders[n];

                            for (var i = 0; i < virtualFolder.Locations.length; i++) {

                                var location = {
                                    value: virtualFolder.Locations[i],
                                    display: virtualFolder.Name + ': ' + virtualFolder.Locations[i]
                                };

                                if (organizerTypeSelect.value === 'Movie' &&
                                    virtualFolder.CollectionType === 'movies' ||
                                    organizerTypeSelect.value === 'Series' &&
                                    virtualFolder.CollectionType === 'tvshows' ||
                                    virtualFolder.Name === "Mixed content") {
                                    targetFolderSelect.innerHTML += '<option value="' + location.value + '">' + location.display + '</option>';
                                }

                            }
                        }
                    } else {
                        targetFolderSelect.innerHTML = '';
                    }
                });


            dlg.querySelector('.btnCloseDialog').addEventListener('click',
                () => {
                    dialogHelper.close(dlg);
                });

            dlg.querySelector('#cancelButton').addEventListener('click',
                () => {
                    dialogHelper.close(dlg);
                })

            dlg.querySelector('#okButton').addEventListener('click',
                async () => {
                   
                   

                    var targetFolder = targetFolderSelect.options[targetFolderSelect.selectedIndex].value;
                    var organizerType = organizerTypeSelect.value;
                    var keyWords = fileNameContainsInput.value.split(';');

                    var options = {
                        TargetFolder: targetFolder,
                        Matches: keyWords,
                        Type: organizerType,
                        Id: id
                    }

                    await ApiClient.saveCustomSmartMatchEntry(options);

                    dialogHelper.close(dlg);
                });

            

            dialogHelper.open(dlg);
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

        function removeSmartMatchEntry(view, index, matchIndex) {
            var info = currentResult.Items[index];
            var entries = [
                {
                    Name: info.Id,
                    Value: info.MatchStrings[matchIndex]
                }];

            ApiClient.deleteSmartMatchEntries(entries).then(function () {

                reloadList(view);

            }, Dashboard.processErrorResponse);
        }
        return function (view, params) {

            var self = this;
            
            
            var smartMatches = view.querySelector('.divMatchInfos');
            var customSmartMatches = view.querySelector('.divCustomMatchInfos');
            
            smartMatches.addEventListener('click', function (e) {
                var button = parentWithClass(e.target, 'btnDeleteMatchEntry');
                if (button) {
                    var index = parseInt(button.getAttribute('data-index'));
                    var matchIndex = parseInt(button.getAttribute('data-matchindex'));
                    removeSmartMatchEntry(view, index, matchIndex);
                }
            });

            customSmartMatches.addEventListener('click', function (e) {
                var button = parentWithClass(e.target, 'btnDeleteMatchEntry');
                if (button) {
                    var index = parseInt(button.getAttribute('data-index'));
                    var matchIndex = parseInt(button.getAttribute('data-matchindex'));
                    removeSmartMatchEntry(view, index, matchIndex);
                }
            });

            view.querySelector('.btnCreateCustomSmartListEntry').addEventListener('click', async () => {
                await openCustomSmartMatchDialog(view);
            });

            view.addEventListener('viewshow', function (e) {

                mainTabsManager.setTabs(this, 2, getTabs);
                loading.show();

                reloadList(view);
            });

            view.addEventListener('viewhide', function (e) {

                currentResult = null;
            });
        };
    });