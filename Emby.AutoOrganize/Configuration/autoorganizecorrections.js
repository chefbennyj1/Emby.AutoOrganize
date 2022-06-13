define(['globalize', 'serverNotifications', 'events', 'scripts/taskbutton', 'datetime', 'loading', 'mainTabsManager', 'dialogHelper', 'paper-icon-button-light', 'formDialogStyle', 'emby-linkbutton', 'detailtablecss', 'emby-collapse', 'emby-input'],
    function (globalize, serverNotifications, events, taskButton, datetime, loading, mainTabsManager, dialogHelper) {


        ApiClient.getFilePathCorrections = function () {
            const url = this.getUrl("Library/FileOrganizations/FileNameCorrections");
            return this.getJSON(url);
        };

        ApiClient.performCorrectionUpdate = function (ids) {
            const url = this.getUrl("Library/FileOrganizations/FileNameCorrections/Update");
            const options = {
                Ids: ids
            };
            return this.ajax({
                type: "POST",
                url: url,
                data: JSON.stringify(options),
                contentType: 'application/json'
            });

        };
        
        function renderTableItemsHtml(corrections) {
            var html = '';

            const groupBySeries = corrections.reduce((group, correction) => {
                const { SeriesName } = correction;
                group[SeriesName] = group[SeriesName] ?? [];
                group[SeriesName].push(correction);
                return group;
            }, {});


            for (const [seriesName, groupedCorrections] of Object.entries(groupBySeries)) {

                html += '<div is="emby-collapse" title="' + seriesName + '">';
                html += '<div class="collapseContent correctionInfo">'

                html += '<div class="table detailTable" style="padding-bottom: 2em;">'
                html += '<table class="tblCorrectionResults" style="width:100%">'
                html += '<thead>'
                html += '<tr style="text-align: left;">'
                html += '<th class="detailTableHeaderCell" data-priority="3" style="padding: 2.1px !important;">' 
                html += '<div class="checkboxContainer">'
                html += '<label class="emby-checkbox-label">';
                html += '<input type="checkbox" is="emby-checkbox" class="chkSelectAll emby-checkbox">';
                html += '<span class="checkboxLabel">Select All</span>';
                html += '</label>';
                html += '</div>'; 
                html += '</th> <!--Correction option check boxes-->'
                html += '<th class="detailTableHeaderCell" data-priority="1">Current Path</th>';
                html += '<th class="detailTableHeaderCell" data-priority="1">Path Correction</th>';
                html += '<th class="detailTableHeaderCell" data-priority="1"></th>';
                html += '</tr>';
                html += '</thead>';
                html += '<tbody class="resultBody"> '
                groupedCorrections.forEach(correction => {
                    html +=
                        '<tr class="detailTableBodyRow detailTableBodyRow-shaded" style="color: var(--theme-primary-text);">';

                    //Checkboxes
                    html += '<td class="detailTableBodyCell">';
                    html += '<div class="checkboxContainer" style="margin-bottom:0 !important">';
                    html += '<label>';
                    html += '<input type="checkbox" is="emby-checkbox" class="chkProcessItem" id="' +
                        correction.Id +
                        '"/>';
                    html += '<span>  </span>';
                    html += '</label>';
                    html += '</div>';
                    html += '</td>';

                    //Current Path
                    html += '<td class="detailTableBodyCell">';
                    html += correction.CurrentPath;
                    html += '</td>';

                    //Corrected Path
                    html += '<td class="detailTableBodyCell">';
                    html += correction.CorrectedPath;
                    html += '</td>';

                    html += '</tr>';
                });
                html += '</tbody>';
                html += '</table>'
                html += '</div>'


                html += '</div>';
                html += '</div>';
            };

            return html;
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


        async function updateResults(view) {
            const correctionResults = view.querySelector('.correctionResults');

            const correctionResult = await ApiClient.getFilePathCorrections();

            correctionResults.innerHTML = renderTableItemsHtml(correctionResult.Items);

            view.querySelector('.processSelectedItems').addEventListener('click',
                async () => {
                    var correctionIds = [];
                    
                    correctionResults.querySelectorAll('.chkProcessItem').forEach(checkbox => {
                        if (checkbox.checked) {
                            correctionIds.push(checkbox.id);
                        }
                    });

                    require(['confirm'],
                        function(confirm) {
                            var message =
                                "Please run the Metadata Refresh, and Library Scan Scheduled Tasks, for file name corrections to take effect in the library."
                            confirm(message, "File System Correction").then(async function() {
                                loading.show();
                                await ApiClient.performCorrectionUpdate(correctionIds);

                                await updateResults(view);

                                loading.hide();
                            });
                        });

                });

            view.querySelectorAll('.chkSelectAll').forEach(allCheckbox => {
                allCheckbox.addEventListener('click', (e) => {
                    var target = e.target;
                    var table = target.closest('.tblCorrectionResults');
                    var resultBody = table.querySelector('.resultBody');
                    var results = resultBody.querySelectorAll('.chkProcessItem');
                    if (target.checked) {
                        results.forEach(r => {
                            r.checked = true;
                        })
                    } else {
                        results.forEach(r => {
                            r.checked = false;
                        })
                    }
                })
            })
        }

        return function (view) {

            view.addEventListener('viewshow',
                async function () {

                    const correctionResult = await ApiClient.getFilePathCorrections();

                    addCorrectionsTab = correctionResult.Items.length > 0;
                    mainTabsManager.setTabs(this, 3, getTabs);

                    await updateResults(view);

                });

        }
    });