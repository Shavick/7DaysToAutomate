using UnityEngine;

public class XUiC_UniversalCrafter : XUiController
{
    protected Vector3i blockPos;
    private TileEntityUniversalCrafter te;

    public XUiC_CrafterRecipeList recipeList;
    public XUiC_CrafterIngredientsList ingredientList;
    public float smoothProgress;
    private Vector3i lastInputChestPos = Vector3i.zero;
    private Vector3i lastOutputChestPos = Vector3i.zero;
    private string lastRecipeName = string.Empty;
    private bool lastIsCrafting = false;
    private bool lastDisabledByPlayer = true;
    private bool lastIsWaitingForIngredients = false;
    private int lastInputTargetsSignature = int.MinValue;
    private int lastOutputTargetsSignature = int.MinValue;

    // -------------------------------
    // Init
    // -------------------------------
    public override void Init()
    {
        base.Init();

        var craftBtn = GetChildById("craftbutton")?.ViewComponent as XUiV_Button;
        if (craftBtn != null)
            craftBtn.Controller.OnPress += (c, b) => CraftButton_OnPress();

        var closeBtn = GetChildById("closebutton")?.ViewComponent as XUiV_Button;
        if (closeBtn != null)
            closeBtn.Controller.OnPress += (c, b) =>
                xui.playerUI.windowManager.Close("CrafterInfo");

        var resetBtn = GetChildById("resetbutton")?.ViewComponent as XUiV_Button;
        if (resetBtn != null)
            resetBtn.Controller.OnPress += (c, b) => ResetButton_OnPress();

        recipeList = GetChildByType<XUiC_CrafterRecipeList>();
        ingredientList = GetChildByType<XUiC_CrafterIngredientsList>();

        if (recipeList != null)
        {
            recipeList.crafterUI = this;
            recipeList.ingredientsList = ingredientList;
        }

        if (ingredientList != null)
        {
            ingredientList.crafterUI = this;
        }
    }

    private void ResetButton_OnPress()
    {
        Log.Out("[Crafter] Resetting...");
        // Leave empty for now unless you add a reset request package/action
    }

    // -------------------------------
    // Always get fresh TE instance
    // -------------------------------
    public TileEntityUniversalCrafter GetCrafter()
    {
        if (blockPos == default)
            return null;

        var world = GameManager.Instance?.World;
        if (world == null)
            return null;

        return world.GetTileEntity(blockPos) as TileEntityUniversalCrafter;
    }

    // -------------------------------
    // Craft Button
    // -------------------------------
    private void CraftButton_OnPress()
    {
        Log.Out($"[Crafter][UI] CraftButton_OnPress blockPos={blockPos}");

        var te = GetCrafter();
        Log.Out($"[Crafter][UI] CraftButton_OnPress teNull={te == null}");

        if (te == null)
            return;

        Log.Out($"[Crafter][UI] CraftButton_OnPress isCrafting={te.isCrafting} disabledByPlayer={te.disabledByPlayer} isWaitingForIngredients={te.isWaitingForIngredients}");

        if (te.isCrafting || !te.disabledByPlayer)
        {
            Log.Out("[Crafter][UI] Request disable craft");
            Helper.RequestCrafterSetEnabled(blockPos, false);
            return;
        }

        Log.Out("[Crafter][UI] Request enable craft");
        Helper.RequestCrafterSetEnabled(blockPos, true);
    }

    // -------------------------------
    // OnOpen - syncs UI to TE
    // -------------------------------
    public override void OnOpen()
    {
        base.OnOpen();

        te = GetCrafter();
        Log.Warning($"[Crafter] OnOpen blockPos = {blockPos} teNull = {te == null}");

        if (te == null)
        {
            RefreshBindings(true);
            return;
        }

        // Build local UI from replicated TE state
        if (recipeList != null)
        {
            if (!string.IsNullOrEmpty(te.SelectedRecipeName))
                recipeList.SelectRecipeByName(te.SelectedRecipeName);
            else
                recipeList.ClearSelection();
        }

        if (ingredientList != null)
        {
            if (!string.IsNullOrEmpty(te.SelectedRecipeName))
                ingredientList.ShowIngredients(te._recipe);
            else
                ingredientList.Clear();
        }

        var inputList = GetChildByType<XUiC_InputContainerList>();
        if (inputList != null)
            inputList.SetContext(te, blockPos);

        var outputList = GetChildByType<XUiC_OutputContainerList>();
        if (outputList != null)
        {
            outputList.SetContext(te, blockPos);
            outputList.IsDirty = true;
        }

        RefreshBindings(true);
    }

    // -------------------------------
    // Window Open Entry Point
    // -------------------------------
    public static void Open(EntityPlayerLocal player, Vector3i pos)
    {
        var crafterCtrl = player.playerUI.xui.GetChildByType<XUiC_UniversalCrafter>();
        crafterCtrl.blockPos = pos;

        player.playerUI.windowManager.Open("CrafterInfo", true, false, true);

        var te = crafterCtrl.GetCrafter();

        var recipeList = player.playerUI.xui.GetChildByType<XUiC_CrafterRecipeList>();
        var ingredientList = player.playerUI.xui.GetChildByType<XUiC_CrafterIngredientsList>();

        recipeList?.SetContext(pos);
        if (recipeList != null)
            recipeList.ingredientsList = ingredientList;

        var inputList = player.playerUI.xui.GetChildByType<XUiC_InputContainerList>();
        if (inputList != null)
            inputList.SetContext(te, pos);

        var outputList = player.playerUI.xui.GetChildByType<XUiC_OutputContainerList>();
        if (outputList != null)
        {
            outputList.SetContext(te, pos);
            outputList.IsDirty = true;
        }
    }

    // -------------------------------
        private int BuildInputTargetsSignature(TileEntityUniversalCrafter targetTe)
    {
        unchecked
        {
            int hash = 17;
            var list = targetTe?.availableInputTargets;
            hash = (hash * 31) + (list?.Count ?? 0);

            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    InputTargetInfo target = list[i];
                    if (target == null)
                    {
                        hash = (hash * 31) + 1;
                        continue;
                    }

                    hash = (hash * 31) + target.BlockPos.x;
                    hash = (hash * 31) + target.BlockPos.y;
                    hash = (hash * 31) + target.BlockPos.z;
                    hash = (hash * 31) + target.PipeGraphId.GetHashCode();
                }
            }

            return hash;
        }
    }

    private int BuildOutputTargetsSignature(TileEntityUniversalCrafter targetTe)
    {
        unchecked
        {
            int hash = 17;
            var list = targetTe?.availableOutputTargets;
            hash = (hash * 31) + (list?.Count ?? 0);

            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    OutputTargetInfo target = list[i];
                    if (target == null)
                    {
                        hash = (hash * 31) + 1;
                        continue;
                    }

                    hash = (hash * 31) + target.BlockPos.x;
                    hash = (hash * 31) + target.BlockPos.y;
                    hash = (hash * 31) + target.BlockPos.z;
                    hash = (hash * 31) + (int)target.TransportMode;
                    hash = (hash * 31) + target.PipeGraphId.GetHashCode();
                }
            }

            return hash;
        }
    }

    // -------------------------------
    // -------------------------------
    public override void Update(float _dt)
    {
        base.Update(_dt);

        te = GetCrafter();

        if (te != null)
        {
            bool selectionChanged = false;
            bool stateChanged = false;

            if (te.SelectedInputChestPos != lastInputChestPos)
            {
                lastInputChestPos = te.SelectedInputChestPos;
                selectionChanged = true;
            }

            if (te.SelectedOutputChestPos != lastOutputChestPos)
            {
                lastOutputChestPos = te.SelectedOutputChestPos;
                selectionChanged = true;
            }

            string recipeName = te.SelectedRecipeName ?? string.Empty;
            if (recipeName != lastRecipeName)
            {
                lastRecipeName = recipeName;
                selectionChanged = true;
            }

            if (te.isCrafting != lastIsCrafting)
            {
                lastIsCrafting = te.isCrafting;
                stateChanged = true;
            }

            if (te.disabledByPlayer != lastDisabledByPlayer)
            {
                lastDisabledByPlayer = te.disabledByPlayer;
                stateChanged = true;
            }

            if (te.isWaitingForIngredients != lastIsWaitingForIngredients)
            {
                lastIsWaitingForIngredients = te.isWaitingForIngredients;
                stateChanged = true;
            }

            int inputTargetsSignature = BuildInputTargetsSignature(te);
            if (inputTargetsSignature != lastInputTargetsSignature)
            {
                lastInputTargetsSignature = inputTargetsSignature;
                selectionChanged = true;
            }

            int outputTargetsSignature = BuildOutputTargetsSignature(te);
            if (outputTargetsSignature != lastOutputTargetsSignature)
            {
                lastOutputTargetsSignature = outputTargetsSignature;
                selectionChanged = true;
            }

            if (selectionChanged)
            {
                var inputList = GetChildByType<XUiC_InputContainerList>();
                if (inputList != null)
                    inputList.IsDirty = true;

                var outputList = GetChildByType<XUiC_OutputContainerList>();
                if (outputList != null)
                    outputList.IsDirty = true;

                var recipeListCtrl = GetChildByType<XUiC_CrafterRecipeList>();
                if (recipeListCtrl != null)
                    recipeListCtrl.IsDirty = true;
            }

            if (selectionChanged || stateChanged)
            {
                RefreshBindings(true);
            }

            if (te.NeedsUiRefresh)
            {
                te.NeedsUiRefresh = false;

                var inputList = GetChildByType<XUiC_InputContainerList>();
                if (inputList != null)
                    inputList.IsDirty = true;

                var outputList = GetChildByType<XUiC_OutputContainerList>();
                if (outputList != null)
                    outputList.IsDirty = true;

                var recipeListCtrl = GetChildByType<XUiC_CrafterRecipeList>();
                if (recipeListCtrl != null)
                    recipeListCtrl.IsDirty = true;

                RefreshBindings(true);
            }

            if (te.isCrafting && !te.isWaitingForIngredients)
            {
                float speed = te.GetCraftingSpeed();
                float baseDuration = te.BaseRecipeDuration;
                float actualDuration = speed > 0f ? (baseDuration / speed) : baseDuration;

                if (actualDuration > 0f)
                {
                    ulong ticksPassed = GameManager.Instance.World.worldTime - te.craftStartTime;
                    float secondsPassed = ticksPassed / 20f;

                    float cycleProgress = secondsPassed / actualDuration;
                    smoothProgress = Mathf.Clamp01(cycleProgress - Mathf.Floor(cycleProgress));
                }
                else
                {
                    smoothProgress = 0f;
                }

                RefreshBindings(true);
            }
            else if (smoothProgress != 0f)
            {
                smoothProgress = 0f;
                RefreshBindings(true);
            }
        }
        else if (smoothProgress != 0f)
        {
            smoothProgress = 0f;
            RefreshBindings(true);
        }
    }

    // -------------------------------
    // Binding values
    // -------------------------------
    public override bool GetBindingValueInternal(ref string value, string bindingName)
    {
        var te = GetCrafter();
                if (bindingName == "req_recipe_selected")
        {
            bool hasRecipe = te != null && !string.IsNullOrEmpty(te.SelectedRecipeName);
            value = hasRecipe ? "true" : "false";
            return true;
        }

        if (bindingName == "req_not_recipe_selected")
        {
            bool hasRecipe = te != null && !string.IsNullOrEmpty(te.SelectedRecipeName);
            value = hasRecipe ? "false" : "true";
            return true;
        }
if (bindingName == "recipename")
        {
            if (te != null && !string.IsNullOrEmpty(te.SelectedRecipeName))
            {
                var recipe = te._recipe ?? CraftingManager.GetRecipe(te.SelectedRecipeName);
                value = recipe != null
                    ? Localization.Get(recipe.GetName())
                    : te.SelectedRecipeName;
            }
            else
            {
                value = "No recipe selected";
            }

            return true;
        }

        if (bindingName == "input_chest_selected")
        {
            value = (te != null && te.SelectedInputChestPos != Vector3i.zero) ? "true" : "false";
            return true;
        }

        if (bindingName == "input_chest_not_selected")
        {
            value = (te != null && te.SelectedInputChestPos != Vector3i.zero) ? "false" : "true";
            return true;
        }

        if (bindingName == "output_chest_selected")
        {
            value = (te != null && te.SelectedOutputChestPos != Vector3i.zero) ? "true" : "false";
            return true;
        }

        if (bindingName == "output_chest_not_selected")
        {
            value = (te != null && te.SelectedOutputChestPos != Vector3i.zero) ? "false" : "true";
            return true;
        }

        if (bindingName == "req_has_ingredients")
        {
            bool has = false;

            if (te != null)
            {
                Recipe recipe = te._recipe;

                if (recipe == null && !string.IsNullOrEmpty(te.SelectedRecipeName))
                    recipe = CraftingManager.GetRecipe(te.SelectedRecipeName);

                if (recipe != null)
                    has = te.HasBufferedIngredientsForNextCraft();
            }

            value = has ? "true" : "false";
            return true;
        }

        if (bindingName == "req_not_has_ingredients")
        {
            bool has = false;

            if (te != null)
            {
                Recipe recipe = te._recipe;

                if (recipe == null && !string.IsNullOrEmpty(te.SelectedRecipeName))
                    recipe = CraftingManager.GetRecipe(te.SelectedRecipeName);

                if (recipe != null)
                    has = te.HasBufferedIngredientsForNextCraft();
            }

            value = has ? "false" : "true";
            return true;
        }

        if (bindingName == "craft")
        {
            if (te == null)
            {
                value = "Craft Unavailable";
                return true;
            }

            if (te.isCrafting)
            {
                value = "Stop Crafting";
                return true;
            }

            if (te.isWaitingForIngredients)
            {
                value = "Waiting";
                return true;
            }

            if (te.disabledByPlayer)
            {
                value = "Start Crafting";
                return true;
            }

            value = "Start Crafting";
            return true;
        }

        if (bindingName == "craftername")
        {
            value = te != null ? te.blockValue.Block.GetLocalizedBlockName() : "null";
            return true;
        }

        if (bindingName == "craftprogress")
        {
            value = smoothProgress.ToString();
            return true;
        }

        return false;
    }
}