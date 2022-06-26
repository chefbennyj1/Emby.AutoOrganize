define([
        'loading', 'mainTabsManager', 'globalize', 'dialogHelper', 'formDialogStyle', 'listViewStyle', 'emby-input',
        'emby-select', 'emby-checkbox', 'emby-button', 'emby-collapse', 'emby-toggle'
    ],
    function(loading, mainTabsManager, globalize, dialogHelper, formDialogStyle) {
        

        ApiClient.getFilePathCorrections = function() {
            const url = this.getUrl("Library/FileOrganizations/FileNameCorrections");
            return this.getJSON(url);
        };

        ApiClient.getFileOrganizationResults = function(options) {

            const url = this.getUrl("Library/FileOrganization", options || {});

            return this.getJSON(url);
        };


        ApiClient.getSmartMatchInfos = function() {


            const url = this.getUrl("Library/FileOrganizations/SmartMatches"); 

            return this.ajax({
                type: "GET",
                url: url,
                dataType: "json"
            });
        };

        ApiClient.deleteSmartMatchEntries = function(entry) {

            const url = this.getUrl("Library/FileOrganizations/SmartMatches/Delete");

            return this.ajax({
                type: "POST",
                url: url,
                data: JSON.stringify(entry),
                contentType: "application/json"
            });
        };

        ApiClient.saveCustomSmartMatchEntry = function(options) {

            const url = this.getUrl("Library/FileOrganizations/SmartMatches/Save");

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

        //var query = {
        //    StartIndex: 0,
        //    Limit: 100000
        //};

        //var currentResult;

        //function parentWithClass(elem, className) {

        //    while (!elem.classList || !elem.classList.contains(className)) {
        //        elem = elem.parentNode;

        //        if (!elem) {
        //            return null;
        //        }
        //    }

        //    return elem;
        //}

        async function reloadList(page) {

            loading.show();

            const result = await ApiClient.getSmartMatchInfos();

            if (result) {

                //currentResult = result;

                populateList(page, result);
            }

            loading.hide();
        }

        function getHtmlFromMatchStrings(info) {

            //var matchStringIndex = 0;
            var matchStringHtml = '';
            for (let matchStringIndex = 0; matchStringIndex < info.MatchStrings.length; matchStringIndex++) {

                matchStringHtml += '<div class="listItem">';

                matchStringHtml += '<div class="listItemBody">';

                matchStringHtml += '<div class="listItemBodyText secondary">';

                matchStringHtml += info.MatchStrings[matchStringIndex];

                matchStringHtml += '</div>';

                matchStringHtml += '</div>';

                matchStringHtml += '<button type="button" is="emby-button" class="btnDeleteMatchEntry emby-button" style="padding: 0;" data-id="' +
                    info.Id + '" data-match-string="' + info.MatchStrings[matchStringIndex] + '" title="Delete"><i class="md-icon">delete</i></button>';

                matchStringHtml += '</div>';

            }

            return matchStringHtml;
        }

        function populateList(page, result) {

            var smartMatch = result.Items;

            var matchInfos = page.querySelector('.divMatchInfos');
            var customMatchInfos = page.querySelector('.divCustomMatchInfos');

            matchInfos.innerHTML = '';
            customMatchInfos.innerHTML = '';

            smartMatch.forEach(match => {

                if (match.IsCustomUserDefinedEntry) {
                    var customSmartListHtml = "";
                    customSmartListHtml += '<div class="" style="padding:4%">';
                    customSmartListHtml += '<div>' + (match.TargetFolder) + '</div>';
                    customSmartListHtml += getHtmlFromMatchStrings(match);
                    customSmartListHtml += '</div>';
                    customSmartListHtml += "</div>";
                    customMatchInfos.innerHTML += customSmartListHtml;

                } else {

                    var smartListHtml = "";
                    //smartListHtml += '<div class="" style="padding:4%">';
                    smartListHtml += '<h3 style="border-bottom: 1px solid hsla(var(--background-hue),var(--background-saturation),calc(var(--background-lightness) - 82%),.5);padding: 9px;"">' + match.Name + '</h3>';
                    //smartListHtml += '<div class="collapseContent matchStringInfo">';
                    smartListHtml += getHtmlFromMatchStrings(match);
                    //smartListHtml += '</div>';
                    smartListHtml += '</div>';
                    //smartListHtml += '</div>';
                    matchInfos.innerHTML += smartListHtml;
                }

            });

            [...page.querySelectorAll('.btnDeleteMatchEntry')].forEach(btn => {
                btn.addEventListener('click',
                    async (e) => {
                        var id = e.target.closest('button').dataset.id;
                        var matchString = e.target.closest('button').dataset.matchString;
                        await removeSmartMatchEntry(page, id, matchString);

                        await reloadList(page);
                    });
            });
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
                    if (organizerTypeSelect.value !== '') {
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
                });

            dlg.querySelector('#okButton').addEventListener('click',
                async () => {

                    var targetFolder  = targetFolderSelect.options[targetFolderSelect.selectedIndex].value;
                    var organizerType = organizerTypeSelect.value;
                    var keyWords      = fileNameContainsInput.value.split(';');

                    var options = {
                        TargetFolder: targetFolder,
                        Matches     : keyWords,
                        Type        : organizerType,
                    };

                    await ApiClient.saveCustomSmartMatchEntry(options);

                    reloadList(view);

                    dialogHelper.close(dlg);
                });

            

            dialogHelper.open(dlg);
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

        async function removeSmartMatchEntry(view, id, matchString) {
            
            var entry = {
                Id: id,
                MatchString: matchString
            };

            try {

                await ApiClient.deleteSmartMatchEntries(entry);

            } catch (err) {}

            await reloadList(view);

        }

        return function (view, params) {

            view.addEventListener('viewshow', async function (e) {

                const correction = await ApiClient.getFilePathCorrections();
                addCorrectionsTab = correction.Items.length > 0;
                mainTabsManager.setTabs(this, 2, getTabs);
                loading.show();

                
                view.querySelector('.btnCreateCustomSmartListEntry').addEventListener('click', async () => {
                    await openCustomSmartMatchDialog(view);
                });


                await reloadList(view);

            });

            view.addEventListener('viewhide', function (e) {
                currentResult = null;
            });
        };
    });