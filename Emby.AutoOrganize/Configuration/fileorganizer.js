define(['dialogHelper', 'loading', 'globalize', 'emby-checkbox', 'emby-input', 'emby-button', 'emby-select', 'paper-icon-button-light', 'formDialogStyle', 'emby-scroller'], function (dialogHelper, loading, globalize) {
    
    ApiClient.getFileOrganizationResults = function (options) {

        const url = this.getUrl("Library/FileOrganization", options || {});

        return this.getJSON(url);
    };

    
    ApiClient.performEpisodeOrganization = function (id, options) {

        const url = this.getUrl("Library/FileOrganizations/" + id + "/Episode/Organize");
        
        return this.ajax({
            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: 'application/json'
        });
    };

    ApiClient.performMovieOrganization = function (id, options) {

        const url = this.getUrl("Library/FileOrganizations/" + id + "/Movie/Organize");

        return this.ajax({
            type: "POST",
            url: url,
            data: JSON.stringify(options),
            contentType: 'application/json'
        });
    };

    var chosenType;
    var extractedName;
    var extractedYear;
    var currentNewItem;
   
    function normalizeString(input) {
        if (input === "") return input;
        if (!input) return "";
        const pattern = /(\s|@|&|'|:|\(|\)|-|<|>|#|\.)/g;
        const normalized =  input.replace(pattern, "").toLocaleLowerCase();
        return normalized;
    }
                                               
    function getIconSvg(icon) {
        switch (icon) {
            case "text-box-search-outline": return "M15.5,12C18,12 20,14 20,16.5C20,17.38 19.75,18.21 19.31,18.9L22.39,22L21,23.39L17.88,20.32C17.19,20.75 16.37,21 15.5,21C13,21 11,19 11,16.5C11,14 13,12 15.5,12M15.5,14A2.5,2.5 0 0,0 13,16.5A2.5,2.5 0 0,0 15.5,19A2.5,2.5 0 0,0 18,16.5A2.5,2.5 0 0,0 15.5,14M5,3H19C20.11,3 21,3.89 21,5V13.03C20.5,12.23 19.81,11.54 19,11V5H5V19H9.5C9.81,19.75 10.26,20.42 10.81,21H5C3.89,21 3,20.11 3,19V5C3,3.89 3.89,3 5,3M7,7H17V9H7V7M7,11H12.03C11.23,11.5 10.54,12.19 10,13H7V11M7,15H9.17C9.06,15.5 9,16 9,16.5V17H7V15Z";
            default: "";
        }
        return "";
    }

    function initBaseForm(context, item) {

        var html = '';
        html += '<svg style="width:24px;height:24px; padding-right:1%" viewBox="0 0 24 24">';
        html += '<path fill="green" d="' + getIconSvg("text-box-search-outline") + '" />';
        html += '</svg>';
        html += item.OriginalFileName.toUpperCase();

        context.querySelector('.inputFile').innerHTML = html;

        context.querySelector('#hfResultId').value = item.Id;

        extractedName = item.ExtractedName;
        extractedYear = item.ExtractedYear;
    }

    async function populateBaseItems(context, item = "") {

        loading.show();
        
        var baseItemsSelect = context.querySelector('#selectBaseItems');
        var rootFolderSelect = context.querySelector('#selectRootFolder');

        const virtualFolderResult = await ApiClient.getVirtualFolders();
        const library = await ApiClient.getItems(null,
            {
                recursive: true,
                includeItemTypes: chosenType,
                sortBy: 'SortName',
                Fields: ['ProductionYear', "Path"]

            });

        const libraryItems = library.Items;
       
        //Create the selected item here!
        var optionsHtml = libraryItems.map(function (s) {

            
            //Don't add the production year if the name contains it already. 
            if (s.Name.includes(`(${s.ProductionYear})`)) {
                return '<option data-path="'+ s.Path + '" data-name="' + s.Name + '" data-year="' + s.ProductionYear + '" value="' + s.Id + '">' + s.Name + '</option>';
            }
            return '<option data-name="' + s.Name + '" data-year="' + s.ProductionYear + '" value="' + s.Id + '">' + s.Name + (s.ProductionYear ? ` (${s.ProductionYear})` : "") + '</option>';

        }).join('');

        baseItemsSelect.innerHTML = '<option data-name="" value=""></option>' + optionsHtml;
         
        var virtualFolderLocations = [];

        var virtualFolders = virtualFolderResult.Items; //|| result;
        for (var n = 0; n < virtualFolders.length; n++) {

            var virtualFolder = virtualFolders[n];

            for (var i = 0; i < virtualFolder.Locations.length; i++) {
                
                var location = {
                    value: virtualFolder.Locations[i],
                    display: virtualFolder.Name + ': ' + virtualFolder.Locations[i]
                };

                if (chosenType === 'Movie' && virtualFolder.CollectionType === 'movies'   || 
                    chosenType === 'Series' && virtualFolder.CollectionType === 'tvshows' ||
                    virtualFolder.Name === "Mixed content") {
                    virtualFolderLocations.push(location);
                }

            }

            if (chosenType === 'Movie' || chosenType === 'Series') {
                virtualFolderLocations.sort();
                virtualFolderLocations.reverse();
            }
            
        }

        //virtualFolderLocationsCount = virtualFolderLocations.length;

        console.table(virtualFolderLocations);


        var mediasFolderHtml = virtualFolderLocations.map(function (s) {
            return '<option value="' + s.value + '">' + s.display + '</option>';
        }).join('');

        if (virtualFolderLocations.length > 1) {
            // If the user has multiple folders, add an empty item to enforce a manual selection
            mediasFolderHtml = '<option value=""></option>' + mediasFolderHtml;
        }

        rootFolderSelect.innerHTML = mediasFolderHtml;
        context.querySelector('.selectRootFolderContainer').classList.remove('hide');
        rootFolderSelect.setAttribute('required', 'required');
        rootFolderSelect.value = "";


        var libraryItem = libraryItems.filter(l => normalizeString(l.Name) === (normalizeString(item.ExtractedName)))[0];

        if (libraryItem) { //If the current item to sort is a movie, check the production years match if an item was found in the library.
            if (item.Type === "Movie" && libraryItem.ProductionYear !== item.ExtractedYear) {
                libraryItem = null;
            }
        }

        if (libraryItem) {
            const libraryItemId = libraryItem.Id;
            baseItemsSelect.value = libraryItemId || "";
        }

        if (item.Status !== "UserInputRequired" && libraryItem) {
            if (virtualFolderLocations) {
                let itemLocationFolder = virtualFolderLocations.filter(l => libraryItem.Path.substring(0, l.value.length) === l.value);
                rootFolderSelect.value = itemLocationFolder[0].value || "";
            }
        }

        //Attach an event to the base item select, so when the base item changes we attempt to match the root folder for the user. It's a courtesy. They can change it.
        baseItemsSelect.addEventListener('change', () => {
            if (baseItemsSelect.value !== "") {
                var baseItem = libraryItems.filter(base => base.Id === baseItemsSelect.value)[0];
                if (baseItem && baseItem.Path) {
                    let itemLocationFolder = virtualFolderLocations.filter(l => baseItem.Path.substring(0, l.value.length) === l.value)[0];
                    if (itemLocationFolder) {
                        rootFolderSelect.value = itemLocationFolder.value || "";
                    }
                }
            }
        });

        loading.hide();
    }

    async function initMovieForm(context, item) {

        initBaseForm(context, item);

        chosenType = 'Movie';

        await populateBaseItems(context, item);
    }

    async function initEpisodeForm(context, item) {

        initBaseForm(context, item);

        chosenType = 'Series';


        //if (!item.ExtractedName) { //|| item.ExtractedName.length < 3) {
        //    context.querySelector('.fldRemember').classList.add('hide');
        //}
        //else {
        context.querySelector('.fldRemember').classList.remove('hide');
        //}
        
        context.querySelector('#txtSeason').value = item.ExtractedSeasonNumber;
        context.querySelector('#txtEpisode').value = item.ExtractedEpisodeNumber;
        context.querySelector('#txtEndingEpisode').value = item.ExtractedEndingEpisodeNumber;

        context.querySelector('#chkRememberCorrection').checked = false;

        await populateBaseItems(context, item);
        
    }

    function submitMediaForm(dlg, item) {
                  
        console.table(item);
        var baseItemSelect    = dlg.querySelector('#selectBaseItems');
        var mediaFolderSelect = dlg.querySelector('#selectRootFolder');
        var resultId          = dlg.querySelector('#hfResultId').value;
        var mediaId           = baseItemSelect.options[baseItemSelect.selectedIndex].value;

        
        //var newMediaName = null;
        //var newMediaYear = null;

        if (mediaId === "##NEW##" && currentNewItem != null) {
            mediaId = ""; //Empty
            //newMediaName = currentNewItem.Name;
            //newMediaYear = currentNewItem.ProductionYear;
        }

        var options = {
            CreateNewDestination : false,
            TargetFolder         : mediaFolderSelect.options[mediaFolderSelect.selectedIndex].value,
            RequestToMoveFile    : true,
            Name                 : baseItemSelect.options[baseItemSelect.selectedIndex].dataset.name,
            Year                 : baseItemSelect.options[baseItemSelect.selectedIndex].dataset.year,
            ProviderIds          : currentNewItem ? currentNewItem.ProviderIds : []
        };

        switch (chosenType) {
            case "Series":

                options.SeriesId            = mediaId;
                options.SeasonNumber        = dlg.querySelector('#txtSeason').value;
                options.EpisodeNumber       = dlg.querySelector('#txtEpisode').value;
                options.EndingEpisodeNumber = dlg.querySelector('#txtEndingEpisode').value;
                options.RememberCorrection  = dlg.querySelector('#chkRememberCorrection').checked;
                
                break;

            case "Movie":

                options.MovieId = mediaId;

                break;
        }

        if (options.TargetFolder === "") {
            Dashboard.alert({
                title: "Auto Organize",
                message:"Target folder can not be empty."
            });
        };

        var message = "";
       
        //Has the user changed the target folder path
        if (item.TargetPath && options.TargetFolder) {
            if (item.TargetPath.substring(0, options.TargetFolder.length) !== options.TargetFolder) {
                //The user has changed the target folder
                options.CreateNewDestination = true;
            }
        } 
       
        console.table(options);

        if (options.CreateNewDestination) {
            message = `The ${chosenType} ${options.Name} ${options.Year ?? ''} current target folder is <br/> ${item.TargetPath.substring(0, options.TargetFolder.length)}<br/> but the file for the ${chosenType} will be created in<br/>${options.TargetFolder}.`;
        } else {
            message = `The following ${item.Type} will be moved to: ${options.TargetFolder}`;
        }

        //This is a new item without a target path figured out yet.
        //if (!item.TargetPath && options.TargetFolder) {
        //    message = 'The following ' + item.Type + ' ' + options.Name + ' will be moved to: ' + options.TargetFolder;
        //}

        message += '<br/><br/>' + 'Are you sure you wish to proceed?';

        require(['confirm'], function (confirm) {

            confirm(message, options.Name + (options.Year ? ' (' + options.Year + ')' : '')).then(async function () {
                
                switch (chosenType) {
                    case "Movie":                         

                        await ApiClient.performMovieOrganization(resultId, options).then(function () {

                            dlg.submitted = true;
                            dialogHelper.close(dlg);

                        });
                        
                        break;

                    case "Series":
                        
                        await ApiClient.performEpisodeOrganization(resultId, options).then(function () {

                            dlg.submitted = true;
                            dialogHelper.close(dlg);
                            
                        });
                        
                        break;
                }
            });
        });

    }

    function showNewMediaDialog(dlg) {

        const selectRootFolder = dlg.querySelector('#selectRootFolder');
        const selectBaseItems = dlg.querySelector('#selectBaseItems');

        if (selectRootFolder.options.length === 0) {//if (mediasLocationsCount == 0) {

            require(['alert'], function (alert) {
                alert({
                    title: 'Error',
                    text: 'No TV libraries are configured in Emby library setup.'
                });
            });
            return;
        }

        require(['itemIdentifier'], function (itemIdentifier) {

            itemIdentifier.showFindNew(extractedName, extractedYear, chosenType, ApiClient.serverId()).then(function (newItem) {

                if (newItem != null) {
                    currentNewItem = newItem;

                    if (selectBaseItems.options.length > 0) {
                        const itemToSelect = [...selectBaseItems.options].filter(o => normalizeString(o.dataset.name) === normalizeString(newItem.Name) && o.dataset.year == newItem.ProductionYear);
                        if (itemToSelect.length) {
                            selectBaseItems.value = itemToSelect[0].value;
                        } else {
                            //Don't add the production year if the name contains it already. 
                            if (currentNewItem.Name.includes(`(${currentNewItem.ProductionYear})`)) {
                                selectBaseItems.innerHTML += '<option selected data-name="' + currentNewItem.Name + '" data-year="' + currentNewItem.ProductionYear + '" value="##NEW##">' + currentNewItem.Name + '</option>';
                            } else {
                                selectBaseItems.innerHTML += '<option selected data-name="' + currentNewItem.Name + '" data-year="' + currentNewItem.ProductionYear + '" value="##NEW##">' + currentNewItem.Name + (currentNewItem.ProductionYear ? ` (${currentNewItem.ProductionYear})` : "") + '</option>';
                            }
                        }
                    }
                     
                    selectedMediasChanged(dlg);
                }
            });
        });
    }

    function selectedMediasChanged(dlg) {
        var mediasId = dlg.querySelector('#selectBaseItems');
       
        var mediaFolderSelect = dlg.querySelector('.selectRootFolderContainer');

        
        if (mediasId.value === "##NEW##" || mediasId.selectedIndex > 0) {
            mediaFolderSelect.classList.remove('hide');
        }
        else {
            mediaFolderSelect.classList.add('hide');
        }
    }

    async function selectedMediaTypeChanged(dlg, item) {
        const mediaType = dlg.querySelector('#selectMediaType').value;

        switch (mediaType) {
            case "":
                dlg.querySelector('#divPermitChoice').classList.add('hide');
                dlg.querySelector('#divGlobalChoice').classList.add('hide');
                dlg.querySelector('#divEpisodeChoice').classList.add('hide');
                break;
            case "Movie":
                dlg.querySelector('#selectBaseItems').setAttribute('label', 'Movie');
               

                dlg.querySelector('#divPermitChoice').classList.remove('hide');
                dlg.querySelector('#divGlobalChoice').classList.remove('hide');
                dlg.querySelector('#divEpisodeChoice').classList.add('hide');

                dlg.querySelector('#txtSeason').removeAttribute('required');
                dlg.querySelector('#txtEpisode').removeAttribute('required');

                await initMovieForm(dlg, item);

                break;
            case "Episode":
                dlg.querySelector('#selectBaseItems').setAttribute('label', 'Series');
               
                
                dlg.querySelector('#divPermitChoice').classList.remove('hide');
                dlg.querySelector('#divGlobalChoice').classList.remove('hide');
                dlg.querySelector('#divEpisodeChoice').classList.remove('hide');

                dlg.querySelector('#txtSeason').setAttribute('required', 'required');
                dlg.querySelector('#txtEpisode').setAttribute('required', 'required');

                await initEpisodeForm(dlg, item);
                break;
        }
    }

    return {
        show: function (item) {
            return new Promise(function (resolve, reject) {

                extractedName = null;
                extractedYear = null;
                currentNewItem = null;

                var xhr = new XMLHttpRequest();
                xhr.open('GET', Dashboard.getConfigurationResourceUrl('FileOrganizerHtml'), true);

                xhr.onload = async function () {

                    var template = this.response;
                    var dlg = dialogHelper.createDialog({
                        removeOnClose: true,
                        size: 'small'
                    });

                    dlg.classList.add('ui-body-a');
                    dlg.classList.add('background-theme-a');

                    dlg.classList.add('formDialog');

                    var html = '';

                    html += template;

                    dlg.innerHTML = html;

                    dlg.querySelector('.formDialogHeaderTitle').innerHTML = 'Organize';

                    dialogHelper.open(dlg);

                    dlg.addEventListener('close', function () {

                        if (dlg.submitted) {
                            resolve();
                        } else {
                            reject();
                        }
                    });

                    dlg.querySelector('.btnCancel').addEventListener('click', function () {

                        dialogHelper.close(dlg);
                    });

                    dlg.querySelector('form').addEventListener('submit', function (elem) {

                        submitMediaForm(dlg, item);

                        elem.preventDefault();
                        return false;
                    });

                    dlg.querySelector('#btnNewMedia').addEventListener('click', function () {

                        showNewMediaDialog(dlg);
                    });

                    dlg.querySelector('#selectBaseItems').addEventListener('change', async function () {

                        await selectedMediasChanged(dlg);
                    });

                    dlg.querySelector('#selectMediaType').addEventListener('change', async  () => {

                        await selectedMediaTypeChanged(dlg, item);

                    });

                    dlg.querySelector('#selectMediaType').value = item.Type !== "Unknown" ? item.Type : "";

                    // Init media type
                    await selectedMediaTypeChanged(dlg, item);
                };

                xhr.send();
            });
        }
    };
});