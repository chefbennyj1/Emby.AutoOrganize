define(['loading', 'mainTabsManager', 'globalize', 'listViewStyle'],
    function(loading, mainTabsManager, globalize) {
        'use strict';

        ApiClient.getFileOrganizationResults = function(options) {

            var url = this.getUrl("Library/FileOrganization", options || {});

            return this.getJSON(url);
        };

        ApiClient.deleteOriginalFileFromOrganizationResult = function(id) {

            var url = this.getUrl("Library/FileOrganizations/" + id + "/File");

            return this.ajax({
                type: "DELETE",
                url: url
            });
        };

        ApiClient.clearOrganizationLog = function() {

            var url = this.getUrl("Library/FileOrganizations");

            return this.ajax({
                type: "DELETE",
                url: url
            });
        };

        ApiClient.performOrganization = function(id) {

            var url = this.getUrl("Library/FileOrganizations/" + id + "/Organize");

            return this.ajax({
                type: "POST",
                url: url
            });
        };

        ApiClient.performEpisodeOrganization = function(id, options) {

            var url = this.getUrl("Library/FileOrganizations/" + id + "/Episode/Organize");

            return this.ajax({
                type: "POST",
                url: url,
                data: JSON.stringify(options),
                contentType: 'application/json'
            });
        };

        ApiClient.performMovieOrganization = function(id, options) {

            var url = this.getUrl("Library/FileOrganizations/" + id + "/Movie/Organize");

            return this.ajax({
                type: "POST",
                url: url,
                data: JSON.stringify(options),
                contentType: 'application/json'
            });
        };

        ApiClient.getSmartMatchInfos = function(options) {

            options = options || {};

            var url = this.getUrl("Library/FileOrganizations/SmartMatches", options);

            return this.ajax({
                type: "GET",
                url: url,
                dataType: "json"
            });
        };

        ApiClient.deleteSmartMatchEntries = function(entries) {

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

            ApiClient.getSmartMatchInfos(query).then(function(infos) {

                    currentResult = infos;

                    populateList(page, infos);

                    loading.hide();

                },
                function() {

                    loading.hide();
                });
        }

        function getHtmlFromMatchStrings(info, i) {

            var matchStringIndex = 0;

            return info.MatchStrings.map(function(m) {

                var matchStringHtml = '';

                matchStringHtml +=
                    '<div class="listItem" style="border-bottom: 1px solid var(--theme-icon-focus-background)">';

                matchStringHtml += '<svg style="width:24px;height:24px" viewBox="0 0 24 24"> ';
                matchStringHtml +=
                    '<path fill="var(--theme-primary-color)" d="M12,6A6,6 0 0,1 18,12C18,14.22 16.79,16.16 15,17.2V19A1,1 0 0,1 14,20H10A1,1 0 0,1 9,19V17.2C7.21,16.16 6,14.22 6,12A6,6 0 0,1 12,6M14,21V22A1,1 0 0,1 13,23H11A1,1 0 0,1 10,22V21H14M20,11H23V13H20V11M1,11H4V13H1V11M13,1V4H11V1H13M4.92,3.5L7.05,5.64L5.63,7.05L3.5,4.93L4.92,3.5M16.95,5.63L19.07,3.5L20.5,4.93L18.37,7.05L16.95,5.63Z" />';
                matchStringHtml += '</svg> ';

                matchStringHtml += '<div class="listItemBody">';

                matchStringHtml += "<div class='listItemBodyText secondary'>" + m + "</div>";

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

            if (infos.length > 0) {
                infos = infos.sort(function(a, b) {

                    a = a.OrganizerType + " " + (a.DisplayName || a.ItemName);
                    b = b.OrganizerType + " " + (b.DisplayName || b.ItemName);

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

            infos.forEach(info => 
            {
                //html += '<div style="">';
                //html += '<img src="' + ApiClient.getUrl("Items/" + info.Id + "/Images/Logo?maxWidth=300") + '" />';
                //html += '</div>';
                html += '<div is="emby-collapse" title="' + (info.DisplayName || info.ItemName) + '">';
                html += '<div class="collapseContent">';
                html += '<div style="">';
                html += getHtmlFromMatchStrings(info, i);
                html += '</div>';
                html += '</div>';
                html += '</div>';
                i++;

                //html += '<div class="listItem">';

                //html += '<div class="listItemIconContainer">';
                //html += '<i class="listItemIcon md-icon">folder</i>';
                //html += '</div>';

                //html += '<div class="listItemBody">';
                //html += "<h2 class='listItemBodyText'>" + (info.DisplayName || info.ItemName) + "</h2>";
                //html += '</div>';

                //html += '</div>';


                //html += getHtmlFromMatchStrings(info, i);
            });

         
            html += "</div>";
        

        var matchInfos = page.querySelector('.divMatchInfos');
        matchInfos.innerHTML = html;
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

        var self = this;

        var divInfos = view.querySelector('.divMatchInfos');

        divInfos.addEventListener('click', function (e) {

            var button = parentWithClass(e.target, 'btnDeleteMatchEntry');

            if (button) {

                var index = parseInt(button.getAttribute('data-index'));
                var matchIndex = parseInt(button.getAttribute('data-matchindex'));

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